using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.IO;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class GoogleSheetsService
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;

        public GoogleSheetsService(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            try
            {
                var settings = GetValidatedSettings();
                var service = CreateClient(settings);
                var spreadsheet = await GetSpreadsheetMetadataAsync(service, settings.SpreadsheetId!);
                string tabName = settings.PurchaseSheetName ?? "Pembelian";
                bool hasTab = spreadsheet.Sheets?.Any(sheet =>
                    string.Equals(sheet.Properties?.Title, tabName, StringComparison.OrdinalIgnoreCase)) == true;

                return hasTab
                    ? (true, $"Koneksi berhasil. Spreadsheet \"{spreadsheet.Properties?.Title}\" dan tab \"{tabName}\" dapat diakses.")
                    : (false, $"Spreadsheet dapat diakses, tetapi tab \"{tabName}\" belum ada.");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Google Sheets test gagal: {ex.Message}", "GoogleSheets", ex.ToString());
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string Message)> AppendPurchaseRowsAsync(
            IEnumerable<BulkDocumentItemResult> items,
            string? documentNumber,
            string? supplierName,
            DateTime? receiptDate)
        {
            try
            {
                var settings = GetValidatedSettings(requireEnabled: true);
                var rows = items?.ToList() ?? new List<BulkDocumentItemResult>();
                if (rows.Count == 0)
                {
                    return (false, "Tidak ada item pembelian untuk dikirim ke Google Sheets.");
                }

                var service = CreateClient(settings);
                string sheetName = settings.PurchaseSheetName ?? "Pembelian";
                await EnsureSheetExistsAsync(service, settings.SpreadsheetId!, sheetName);
                await EnsurePurchaseHeaderAsync(service, settings.SpreadsheetId!, sheetName);

                var valueRange = new ValueRange
                {
                    Values = rows.Select(item => (IList<object>)new List<object>
                    {
                        (receiptDate ?? DateTime.Now).ToString("yyyy-MM-dd"),
                        documentNumber ?? string.Empty,
                        supplierName ?? string.Empty,
                        item.ProductName,
                        item.Quantity,
                        item.Unit ?? string.Empty,
                        item.Price,
                        item.Total,
                        "SYNCED"
                    }).ToList()
                };

                var appendRequest = service.Spreadsheets.Values.Append(
                    valueRange,
                    settings.SpreadsheetId,
                    $"{EscapeSheetName(sheetName)}!A:I");
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                appendRequest.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
                await appendRequest.ExecuteAsync();

                return (true, $"Berhasil mengirim {rows.Count} baris ke Google Sheets tab \"{sheetName}\".");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Google Sheets append gagal: {ex.Message}", "GoogleSheets", ex.ToString());
                return (false, ex.Message);
            }
        }

        private GoogleSheetsSettings GetValidatedSettings(bool requireEnabled = false)
        {
            var settings = _configService.Config?.GoogleSheets ?? new GoogleSheetsSettings();
            if (requireEnabled && !settings.Enabled)
            {
                throw new InvalidOperationException("Google Sheets belum diaktifkan di Settings.");
            }

            if (string.IsNullOrWhiteSpace(settings.CredentialsJsonPath))
            {
                throw new InvalidOperationException("Path service account JSON belum diisi.");
            }

            if (string.IsNullOrWhiteSpace(settings.SpreadsheetId))
            {
                throw new InvalidOperationException("Spreadsheet ID belum diisi.");
            }

            return settings;
        }

        private SheetsService CreateClient(GoogleSheetsSettings settings)
        {
            string credentialsPath = ResolvePath(settings.CredentialsJsonPath!);
            if (!File.Exists(credentialsPath))
            {
                throw new FileNotFoundException($"File credentials Google Sheets tidak ditemukan: {credentialsPath}");
            }

            using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var credential = GoogleCredential.FromStream(stream)
                .CreateScoped(SheetsService.Scope.Spreadsheets);

            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SmartSembakoAssistant"
            });
        }

        private async Task<Spreadsheet> GetSpreadsheetMetadataAsync(SheetsService service, string spreadsheetId)
        {
            var request = service.Spreadsheets.Get(spreadsheetId);
            request.Fields = "properties.title,sheets.properties.title";
            return await request.ExecuteAsync();
        }

        private async Task EnsureSheetExistsAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var spreadsheet = await GetSpreadsheetMetadataAsync(service, spreadsheetId);
            bool exists = spreadsheet.Sheets?.Any(sheet =>
                string.Equals(sheet.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase)) == true;
            if (exists)
            {
                return;
            }

            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
                {
                    new()
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties
                            {
                                Title = sheetName
                            }
                        }
                    }
                }
            };

            var request = service.Spreadsheets.BatchUpdate(batch, spreadsheetId);
            await request.ExecuteAsync();
        }

        private async Task EnsurePurchaseHeaderAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var getRequest = service.Spreadsheets.Values.Get(spreadsheetId, $"{EscapeSheetName(sheetName)}!A1:I1");
            var current = await getRequest.ExecuteAsync();
            if (current.Values?.Count > 0)
            {
                return;
            }

            var header = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        "Tanggal",
                        "No Dokumen",
                        "Supplier",
                        "Produk",
                        "Qty",
                        "Satuan",
                        "Harga Satuan",
                        "Total",
                        "Status"
                    }
                }
            };

            var updateRequest = service.Spreadsheets.Values.Update(
                header,
                spreadsheetId,
                $"{EscapeSheetName(sheetName)}!A1:I1");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await updateRequest.ExecuteAsync();
        }

        private static string EscapeSheetName(string sheetName)
        {
            return $"'{sheetName.Replace("'", "''")}'";
        }

        private static string ResolvePath(string configuredPath)
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath));
        }
    }
}
