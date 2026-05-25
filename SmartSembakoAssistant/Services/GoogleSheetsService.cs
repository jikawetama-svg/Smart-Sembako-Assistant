using System.IO;
using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class GoogleSheetsService
    {
        private const int MaxRowsPerRequest = 500;
        private static readonly IReadOnlyList<string> PurchaseHeaders = new[]
        {
            "Tanggal",
            "No Dokumen",
            "Supplier",
            "Produk",
            "Qty",
            "Satuan",
            "Harga Satuan",
            "Total",
            "Status",
            "ProductId",
            "OldStock",
            "NewStock",
            "Source",
            "CorrelationId",
            "SyncedAt"
        };

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
                using var service = CreateClient(settings);
                var spreadsheet = await ExecuteWithRetryAsync(
                    () => GetSpreadsheetMetadataAsync(service, settings.SpreadsheetId!),
                    "get spreadsheet metadata");
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

        public async Task EnsureSheetWithHeaderAsync(string sheetName, IReadOnlyList<string> headers)
        {
            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await EnsureSheetWithHeaderAsync(service, settings.SpreadsheetId!, sheetName, headers);
        }

        public async Task ReplaceSheetRowsAsync(
            string sheetName,
            IReadOnlyList<string> headers,
            IEnumerable<IReadOnlyList<object>> rows)
        {
            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await EnsureSheetWithHeaderAsync(service, settings.SpreadsheetId!, sheetName, headers);
            await ClearDataRowsAsync(service, settings.SpreadsheetId!, sheetName);
            await WriteRowsAsync(service, settings.SpreadsheetId!, sheetName, rows, startRow: 2);
        }

        public async Task<int> UpsertRowsAsync(
            string sheetName,
            IReadOnlyList<string> headers,
            IReadOnlyList<string> keyColumns,
            IEnumerable<IReadOnlyDictionary<string, object>> rows)
        {
            var incoming = rows?.ToList() ?? new List<IReadOnlyDictionary<string, object>>();
            if (incoming.Count == 0)
            {
                await EnsureSheetWithHeaderAsync(sheetName, headers);
                return 0;
            }

            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await EnsureSheetWithHeaderAsync(service, settings.SpreadsheetId!, sheetName, headers);

            var effectiveHeaders = await ReadHeaderAsync(service, settings.SpreadsheetId!, sheetName);
            var allRows = await ReadDataRowsAsDictionariesAsync(service, settings.SpreadsheetId!, sheetName, effectiveHeaders);
            var byKey = allRows
                .Where(row => keyColumns.All(key => row.ContainsKey(key)))
                .ToDictionary(row => BuildKey(keyColumns.Select(key => row.GetValueOrDefault(key))), row => row, StringComparer.OrdinalIgnoreCase);

            foreach (var row in incoming)
            {
                byKey[BuildKey(keyColumns.Select(key => row.GetValueOrDefault(key)))] = row;
            }

            var outputRows = byKey.Values
                .Select(row => (IReadOnlyList<object>)effectiveHeaders.Select(header => row.GetValueOrDefault(header) ?? string.Empty).ToList())
                .ToList();

            await ClearDataRowsAsync(service, settings.SpreadsheetId!, sheetName);
            await WriteRowsAsync(service, settings.SpreadsheetId!, sheetName, outputRows, startRow: 2);
            return incoming.Count;
        }

        public async Task<int> AppendRowsAsync(string sheetName, IEnumerable<IReadOnlyList<object>> rows)
        {
            var rowList = rows?.ToList() ?? new List<IReadOnlyList<object>>();
            if (rowList.Count == 0)
            {
                return 0;
            }

            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await EnsureSheetExistsAsync(service, settings.SpreadsheetId!, sheetName);

            int appended = 0;
            int maxColumns = Math.Max(1, rowList.Max(row => row.Count));
            foreach (var chunk in ChunkRows(rowList, MaxRowsPerRequest))
            {
                var valueRange = new ValueRange
                {
                    Values = chunk.Select(row => (IList<object>)row.ToList()).ToList()
                };

                var appendRequest = service.Spreadsheets.Values.Append(
                    valueRange,
                    settings.SpreadsheetId,
                    BuildA1Range(sheetName, 1, 1, null, maxColumns));
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                appendRequest.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
                await ExecuteWithRetryAsync(() => appendRequest.ExecuteAsync(), $"append rows to {sheetName}");
                appended += chunk.Count;
                await DelayBetweenBatchesAsync();
            }

            return appended;
        }

        public async Task<int> AppendRowsWithDedupeAsync(
            string sheetName,
            IReadOnlyList<string> keyColumns,
            IEnumerable<IReadOnlyDictionary<string, object>> rows)
        {
            var incoming = rows?.ToList() ?? new List<IReadOnlyDictionary<string, object>>();
            if (incoming.Count == 0)
            {
                return 0;
            }

            var headers = incoming
                .SelectMany(row => row.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await EnsureSheetWithHeaderAsync(service, settings.SpreadsheetId!, sheetName, headers);

            var effectiveHeaders = await ReadHeaderAsync(service, settings.SpreadsheetId!, sheetName);
            var existingKeys = await ReadExistingKeysAsync(service, settings.SpreadsheetId!, sheetName, keyColumns, effectiveHeaders);
            var newRows = new List<IReadOnlyList<object>>();

            foreach (var row in incoming)
            {
                string key = BuildKey(keyColumns.Select(column => row.GetValueOrDefault(column)));
                if (existingKeys.Add(key))
                {
                    newRows.Add(effectiveHeaders.Select(header => row.GetValueOrDefault(header) ?? string.Empty).ToList());
                }
            }

            return await AppendRowsAsync(sheetName, newRows);
        }

        public async Task ClearDataRowsAsync(string sheetName)
        {
            var settings = GetValidatedSettings(requireEnabled: true);
            using var service = CreateClient(settings);
            await ClearDataRowsAsync(service, settings.SpreadsheetId!, sheetName);
        }

        public async Task<(bool Success, string Message)> AppendPurchaseRowsAsync(
            IEnumerable<BulkDocumentItemResult> items,
            string? documentNumber,
            string? supplierName,
            DateTime? receiptDate)
        {
            try
            {
                var rows = items?.ToList() ?? new List<BulkDocumentItemResult>();
                if (rows.Count == 0)
                {
                    return (false, "Tidak ada item pembelian untuk dikirim ke Google Sheets.");
                }

                string sheetName = _configService.Config?.GoogleSheets?.PurchaseSheetName ?? "Pembelian";
                string syncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var payload = rows.Select(item => (IReadOnlyDictionary<string, object>)new Dictionary<string, object>
                {
                    ["Tanggal"] = (receiptDate ?? DateTime.Now).ToString("yyyy-MM-dd"),
                    ["No Dokumen"] = documentNumber ?? string.Empty,
                    ["Supplier"] = supplierName ?? string.Empty,
                    ["Produk"] = item.ProductName,
                    ["Qty"] = item.Quantity,
                    ["Satuan"] = item.Unit ?? string.Empty,
                    ["Harga Satuan"] = item.Price,
                    ["Total"] = item.Total,
                    ["Status"] = "SYNCED",
                    ["ProductId"] = item.ProductId,
                    ["OldStock"] = item.OldStock,
                    ["NewStock"] = item.NewStock,
                    ["Source"] = "OCR/Purchase",
                    ["CorrelationId"] = string.Empty,
                    ["SyncedAt"] = syncedAt
                }).ToList();

                await EnsureSheetWithHeaderAsync(sheetName, PurchaseHeaders);
                int appended = await AppendRowsWithDedupeAsync(
                    sheetName,
                    new[] { "No Dokumen", "Produk", "Qty", "Total" },
                    payload);

                return (true, appended == 0
                    ? $"Tidak ada baris baru untuk tab \"{sheetName}\" karena semua data sudah pernah dikirim."
                    : $"Berhasil mengirim {appended} baris baru ke Google Sheets tab \"{sheetName}\".");
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

        private async Task EnsureSheetWithHeaderAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IReadOnlyList<string> headers)
        {
            await EnsureSheetExistsAsync(service, spreadsheetId, sheetName);
            var currentHeader = await ReadHeaderAsync(service, spreadsheetId, sheetName);

            if (currentHeader.Count == 0)
            {
                await WriteHeaderAsync(service, spreadsheetId, sheetName, headers);
                return;
            }

            var merged = currentHeader.ToList();
            bool changed = false;
            foreach (string header in headers.Where(header => !string.IsNullOrWhiteSpace(header)))
            {
                if (!merged.Any(existing => string.Equals(existing, header, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(header);
                    changed = true;
                }
            }

            if (changed)
            {
                await WriteHeaderAsync(service, spreadsheetId, sheetName, merged);
            }
        }

        private async Task EnsureSheetExistsAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var spreadsheet = await ExecuteWithRetryAsync(
                () => GetSpreadsheetMetadataAsync(service, spreadsheetId),
                "get spreadsheet metadata");
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
            await ExecuteWithRetryAsync(() => request.ExecuteAsync(), $"create sheet {sheetName}");
        }

        private async Task<List<string>> ReadHeaderAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var getRequest = service.Spreadsheets.Values.Get(spreadsheetId, $"{EscapeSheetName(sheetName)}!1:1");
            var current = await ExecuteWithRetryAsync(() => getRequest.ExecuteAsync(), $"read header {sheetName}");
            return current.Values?.FirstOrDefault()?
                .Select(value => value?.ToString()?.Trim() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList() ?? new List<string>();
        }

        private async Task WriteHeaderAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IReadOnlyList<string> headers)
        {
            var header = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    headers.Select(header => (object)header).ToList()
                }
            };

            var updateRequest = service.Spreadsheets.Values.Update(
                header,
                spreadsheetId,
                BuildA1Range(sheetName, 1, 1, 1, headers.Count));
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await ExecuteWithRetryAsync(() => updateRequest.ExecuteAsync(), $"write header {sheetName}");
        }

        private async Task ClearDataRowsAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var clearRequest = service.Spreadsheets.Values.Clear(
                new ClearValuesRequest(),
                spreadsheetId,
                $"{EscapeSheetName(sheetName)}!A2:ZZ");
            await ExecuteWithRetryAsync(() => clearRequest.ExecuteAsync(), $"clear rows {sheetName}");
        }

        private async Task<HashSet<string>> ReadExistingKeysAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IReadOnlyList<string> keyColumns,
            IReadOnlyList<string> headers)
        {
            var rows = await ReadDataRowsAsDictionariesAsync(service, spreadsheetId, sheetName, headers);
            return rows
                .Select(row => BuildKey(keyColumns.Select(column => row.GetValueOrDefault(column))))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<IReadOnlyDictionary<string, object>>> ReadDataRowsAsDictionariesAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IReadOnlyList<string> headers)
        {
            if (headers.Count == 0)
            {
                return new List<IReadOnlyDictionary<string, object>>();
            }

            var request = service.Spreadsheets.Values.Get(
                spreadsheetId,
                BuildA1Range(sheetName, 2, 1, null, headers.Count));
            var response = await ExecuteWithRetryAsync(() => request.ExecuteAsync(), $"read rows {sheetName}");
            var result = new List<IReadOnlyDictionary<string, object>>();

            foreach (var values in response.Values ?? new List<IList<object>>())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                {
                    row[headers[i]] = i < values.Count ? values[i] : string.Empty;
                }

                if (row.Values.Any(value => !string.IsNullOrWhiteSpace(value?.ToString())))
                {
                    result.Add(row);
                }
            }

            return result;
        }

        private async Task WriteRowsAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IEnumerable<IReadOnlyList<object>> rows,
            int startRow)
        {
            var rowList = rows?.ToList() ?? new List<IReadOnlyList<object>>();
            if (rowList.Count == 0)
            {
                return;
            }

            int maxColumns = Math.Max(1, rowList.Max(row => row.Count));
            int currentRow = startRow;
            foreach (var chunk in ChunkRows(rowList, MaxRowsPerRequest))
            {
                var valueRange = new ValueRange
                {
                    Values = chunk.Select(row => (IList<object>)row.ToList()).ToList()
                };

                var updateRequest = service.Spreadsheets.Values.Update(
                    valueRange,
                    spreadsheetId,
                    BuildA1Range(sheetName, currentRow, 1, currentRow + chunk.Count - 1, maxColumns));
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await ExecuteWithRetryAsync(() => updateRequest.ExecuteAsync(), $"write rows {sheetName}");
                currentRow += chunk.Count;
                await DelayBetweenBatchesAsync();
            }
        }

        private async Task ExecuteWithRetryAsync(Func<Task> action, string operation)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await action();
                return true;
            }, operation);
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operation)
        {
            Exception? lastError = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (GoogleApiException ex) when (IsRetryable(ex) && attempt < 3)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }

            throw lastError ?? new InvalidOperationException($"Google Sheets operation failed: {operation}");
        }

        private static bool IsRetryable(GoogleApiException ex)
        {
            return ex.HttpStatusCode == HttpStatusCode.TooManyRequests || (int)ex.HttpStatusCode >= 500;
        }

        private static Task DelayBetweenBatchesAsync()
        {
            return Task.Delay(150);
        }

        private static List<List<T>> ChunkRows<T>(IReadOnlyList<T> rows, int chunkSize)
        {
            var chunks = new List<List<T>>();
            for (int i = 0; i < rows.Count; i += chunkSize)
            {
                chunks.Add(rows.Skip(i).Take(chunkSize).ToList());
            }

            return chunks;
        }

        private static string BuildA1Range(string sheetName, int startRow, int startColumn, int? endRow, int endColumn)
        {
            string start = $"{ColumnName(startColumn)}{startRow}";
            string end = $"{ColumnName(endColumn)}{(endRow.HasValue ? endRow.Value.ToString() : string.Empty)}";
            return $"{EscapeSheetName(sheetName)}!{start}:{end}";
        }

        private static string ColumnName(int columnNumber)
        {
            string columnName = string.Empty;
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                columnNumber = (columnNumber - modulo) / 26;
            }

            return columnName;
        }

        private static string BuildKey(IEnumerable<object?> values)
        {
            return string.Join("|", values.Select(value => NormalizeKeyValue(value?.ToString())));
        }

        private static string NormalizeKeyValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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
