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
        private static readonly IReadOnlyList<string> PurchaseHeaders = GoogleSheetsSchema.PurchaseHeaders;

        private sealed class ManagedConditionalRule
        {
            public int Column { get; init; }
            public string ConditionType { get; init; } = "TEXT_EQ";
            public string Value { get; init; } = "";
            public float Red { get; init; }
            public float Green { get; init; }
            public float Blue { get; init; }
        }

        private sealed class ManagedChart
        {
            public string Title { get; init; } = "";
            public string ChartType { get; init; } = "LINE";
            public int DomainColumn { get; init; }
            public IReadOnlyList<int> SeriesColumns { get; init; } = Array.Empty<int>();
            public int AnchorRow { get; init; }
            public int AnchorColumn { get; init; }
        }

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
            DateTime? receiptDate,
            IEnumerable<PurchaseSheetRowMetadata>? metadata = null)
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
                var metadataList = (metadata ?? Enumerable.Empty<PurchaseSheetRowMetadata>()).ToList();
                var metadataByLine = metadataList
                    .Where(item => item.LineIndex.HasValue)
                    .GroupBy(item => item.LineIndex!.Value)
                    .ToDictionary(group => group.Key, group => group.First());
                var payload = rows.Select((item, index) =>
                {
                    int lineIndex = index + 1;
                    PurchaseSheetRowMetadata? rowMeta = null;
                    if (!metadataByLine.TryGetValue(lineIndex, out rowMeta))
                    {
                        rowMeta = index < metadataList.Count
                            ? metadataList[index]
                            : metadataList.FirstOrDefault(meta => meta.ProductId == item.ProductId);
                    }

                    string rawName = rowMeta?.RawOcrName ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        rawName = item.ProductName;
                    }

                    string correlationId = rowMeta?.CorrelationId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(correlationId))
                    {
                        correlationId = BuildPurchaseCorrelationId(documentNumber, receiptDate, lineIndex);
                    }

                    string rowKey = BuildPurchaseRowKey(documentNumber, lineIndex, item.ProductId, item.Quantity, item.Total);

                    return (IReadOnlyDictionary<string, object>)new Dictionary<string, object>
                    {
                        ["Tanggal"] = (receiptDate ?? DateTime.Now).ToString("yyyy-MM-dd"),
                        ["No Dokumen"] = documentNumber ?? string.Empty,
                        ["Supplier"] = supplierName ?? string.Empty,
                        ["Produk"] = item.ProductName,
                        ["Nama OCR Asli"] = rawName,
                        ["Mapping Source"] = rowMeta?.MappingSource ?? "manual",
                        ["Trust Level"] = rowMeta?.TrustLevel ?? "confirmed",
                        ["Qty"] = item.Quantity,
                        ["Satuan"] = item.Unit ?? string.Empty,
                        ["Harga Satuan"] = item.Price,
                        ["Total"] = item.Total,
                        ["Status"] = "SYNCED",
                        ["ProductId"] = item.ProductId,
                        ["LineIndex"] = lineIndex,
                        ["RowKey"] = rowKey,
                        ["OldStock"] = item.OldStock,
                        ["NewStock"] = item.NewStock,
                        ["Source"] = "OCR/Purchase",
                        ["CorrelationId"] = correlationId,
                        ["SyncedAt"] = syncedAt
                    };
                }).ToList();

                await EnsureSheetWithHeaderAsync(sheetName, PurchaseHeaders);
                int appended = await AppendRowsWithDedupeAsync(
                    sheetName,
                    new[] { "RowKey" },
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
            request.Fields = "properties.title,sheets.properties(sheetId,title)";
            return await request.ExecuteAsync();
        }

        private async Task<Sheet?> GetSheetDetailsAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var request = service.Spreadsheets.Get(spreadsheetId);
            request.Fields = "sheets(properties(sheetId,title),conditionalFormats,charts(chartId,spec(title)))";
            var spreadsheet = await ExecuteWithRetryAsync(() => request.ExecuteAsync(), $"get sheet details {sheetName}");
            return spreadsheet.Sheets?
                .FirstOrDefault(sheet => string.Equals(sheet.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase));
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
                await ApplySheetFormattingAsync(service, spreadsheetId, sheetName, headers);
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

            await ApplySheetFormattingAsync(service, spreadsheetId, sheetName, changed ? merged : currentHeader);
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

        private async Task ApplySheetFormattingAsync(
            SheetsService service,
            string spreadsheetId,
            string sheetName,
            IReadOnlyList<string> headers)
        {
            var settings = _configService.Config?.GoogleSheets ?? new GoogleSheetsSettings();
            if (!settings.EnableFormatting || headers.Count == 0)
            {
                return;
            }

            var sheetDetails = await GetSheetDetailsAsync(service, spreadsheetId, sheetName);
            int? sheetId = sheetDetails?.Properties?.SheetId;
            if (!sheetId.HasValue)
            {
                return;
            }

            var requests = new List<Request>
            {
                new()
                {
                    UpdateSheetProperties = new UpdateSheetPropertiesRequest
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = sheetId,
                            GridProperties = new GridProperties { FrozenRowCount = 1 }
                        },
                        Fields = "gridProperties.frozenRowCount"
                    }
                },
                new()
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange
                        {
                            SheetId = sheetId,
                            StartRowIndex = 0,
                            EndRowIndex = 1,
                            StartColumnIndex = 0,
                            EndColumnIndex = headers.Count
                        },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat
                            {
                                BackgroundColor = new Color { Red = 0.11f, Green = 0.2f, Blue = 0.33f },
                                TextFormat = new TextFormat
                                {
                                    Bold = true,
                                    ForegroundColor = new Color { Red = 1f, Green = 1f, Blue = 1f }
                                }
                            }
                        },
                        Fields = "userEnteredFormat(backgroundColor,textFormat)"
                    }
                },
                new()
                {
                    SetBasicFilter = new SetBasicFilterRequest
                    {
                        Filter = new BasicFilter
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = 0,
                                StartColumnIndex = 0,
                                EndColumnIndex = headers.Count
                            }
                        }
                    }
                },
                new()
                {
                    AutoResizeDimensions = new AutoResizeDimensionsRequest
                    {
                        Dimensions = new DimensionRange
                        {
                            SheetId = sheetId,
                            Dimension = "COLUMNS",
                            StartIndex = 0,
                            EndIndex = headers.Count
                        }
                    }
                }
            };

            AddNumberFormatRequests(requests, sheetId.Value, headers);
            if (settings.EnableConditionalFormatting)
            {
                AddConditionalFormatRequests(requests, sheetId.Value, sheetName, headers, sheetDetails?.ConditionalFormats);
            }

            if (settings.EnableCharts)
            {
                AddManagedChartRequests(requests, sheetId.Value, sheetName, headers, sheetDetails?.Charts);
            }

            var request = service.Spreadsheets.BatchUpdate(
                new BatchUpdateSpreadsheetRequest { Requests = requests },
                spreadsheetId);
            await ExecuteWithRetryAsync(() => request.ExecuteAsync(), $"format sheet {sheetName}");
        }

        private async Task<int?> GetSheetIdAsync(SheetsService service, string spreadsheetId, string sheetName)
        {
            var spreadsheet = await ExecuteWithRetryAsync(
                () => GetSpreadsheetMetadataAsync(service, spreadsheetId),
                "get spreadsheet metadata");
            return spreadsheet.Sheets?
                .FirstOrDefault(sheet => string.Equals(sheet.Properties?.Title, sheetName, StringComparison.OrdinalIgnoreCase))
                ?.Properties?.SheetId;
        }

        private static void AddNumberFormatRequests(List<Request> requests, int sheetId, IReadOnlyList<string> headers)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i];
                string? pattern = GetNumberFormatPattern(header);
                if (pattern == null)
                {
                    continue;
                }

                requests.Add(new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange
                        {
                            SheetId = sheetId,
                            StartRowIndex = 1,
                            StartColumnIndex = i,
                            EndColumnIndex = i + 1
                        },
                        Cell = new CellData
                        {
                            UserEnteredFormat = new CellFormat
                            {
                                NumberFormat = new NumberFormat
                                {
                                    Type = GetNumberFormatType(pattern),
                                    Pattern = pattern
                                }
                            }
                        },
                        Fields = "userEnteredFormat.numberFormat"
                    }
                });
            }
        }

        private static string? GetNumberFormatPattern(string header)
        {
            if (header.Contains("SyncedAt", StringComparison.OrdinalIgnoreCase))
            {
                return "yyyy-mm-dd hh:mm:ss";
            }

            if (header.Contains("Tanggal", StringComparison.OrdinalIgnoreCase))
            {
                return "yyyy-mm-dd";
            }

            if (header.Contains("Margin", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("%", StringComparison.OrdinalIgnoreCase))
            {
                return "0.00%";
            }

            if (header.Contains("Omzet", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Profit", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Harga", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Piutang", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Hutang", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("Nilai", StringComparison.OrdinalIgnoreCase))
            {
                return "[$Rp-421] #,##0";
            }

            return null;
        }

        private static string GetNumberFormatType(string pattern)
        {
            if (pattern.Contains('%'))
            {
                return "PERCENT";
            }

            if (pattern.Contains("hh", StringComparison.OrdinalIgnoreCase))
            {
                return "DATE_TIME";
            }

            return pattern.Contains("yyyy", StringComparison.OrdinalIgnoreCase) ? "DATE" : "CURRENCY";
        }

        private void AddConditionalFormatRequests(
            List<Request> requests,
            int sheetId,
            string sheetName,
            IReadOnlyList<string> headers,
            IList<ConditionalFormatRule>? existingRules)
        {
            var managedRules = BuildManagedConditionalRules(sheetName, headers);
            if (managedRules.Count == 0)
            {
                return;
            }

            AddDeleteManagedConditionalRulesRequests(requests, sheetId, existingRules, managedRules);
            foreach (var rule in managedRules)
            {
                AddConditionalRuleRequest(requests, sheetId, rule);
            }
        }

        private List<ManagedConditionalRule> BuildManagedConditionalRules(string sheetName, IReadOnlyList<string> headers)
        {
            var rules = new List<ManagedConditionalRule>();
            if (string.Equals(sheetName, "Stok_Kritis", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedTextRule(rules, headers, "Status Audit", "MINUS", 1f, 0.8f, 0.8f);
                AddManagedTextRule(rules, headers, "Status Audit", "HABIS", 1f, 0.9f, 0.65f);
                AddManagedTextRule(rules, headers, "Status Audit", "RENDAH", 1f, 0.95f, 0.55f);
                AddManagedTextRule(rules, headers, "Status", "Minus", 1f, 0.8f, 0.8f);
                AddManagedTextRule(rules, headers, "Status", "Habis", 1f, 0.9f, 0.65f);
                AddManagedTextRule(rules, headers, "Status", "Rendah", 1f, 0.95f, 0.55f);
            }
            else if (IsPurchaseSheet(sheetName))
            {
                AddManagedTextRule(rules, headers, "Trust Level", "candidate", 1f, 0.95f, 0.55f);
                AddManagedTextRule(rules, headers, "Trust Level", "review", 1f, 0.85f, 0.55f);
                AddManagedTextRule(rules, headers, "Mapping Source", "review-queue", 1f, 0.85f, 0.55f);
            }
            else if (string.Equals(sheetName, "Piutang", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedTextRule(rules, headers, "Status", "OVERDUE", 1f, 0.8f, 0.8f);
                AddManagedTextRule(rules, headers, "Status", "DUE_SOON", 1f, 0.95f, 0.55f);
            }
            else if (string.Equals(sheetName, "Dashboard", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedNumberGreaterRule(rules, headers, "Stok Minus", "0", 1f, 0.8f, 0.8f);
                AddManagedNumberGreaterRule(rules, headers, "Produk Tanpa Modal", "0", 1f, 0.95f, 0.55f);
                AddManagedNumberGreaterRule(rules, headers, "Produk Harga Jual 0/Minus", "0", 1f, 0.9f, 0.65f);
            }
            else if (string.Equals(sheetName, "Produk_Audit", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedTextRule(rules, headers, "Audit Flags", "STOK_MINUS", 1f, 0.8f, 0.8f, "TEXT_CONTAINS");
                AddManagedTextRule(rules, headers, "Audit Flags", "COST_0", 1f, 0.95f, 0.55f, "TEXT_CONTAINS");
                AddManagedTextRule(rules, headers, "Audit Flags", "COST_MINUS", 1f, 0.8f, 0.8f, "TEXT_CONTAINS");
                AddManagedTextRule(rules, headers, "Audit Flags", "HARGA_JUAL_0", 1f, 0.9f, 0.65f, "TEXT_CONTAINS");
                AddManagedTextRule(rules, headers, "Audit Flags", "HARGA_JUAL_MINUS", 1f, 0.8f, 0.8f, "TEXT_CONTAINS");
                AddManagedTextRule(rules, headers, "Audit Flags", "KATEGORI_KOSONG", 1f, 0.95f, 0.55f, "TEXT_CONTAINS");
            }

            return rules;
        }

        private static void AddManagedTextRule(
            List<ManagedConditionalRule> rules,
            IReadOnlyList<string> headers,
            string columnName,
            string value,
            float red,
            float green,
            float blue,
            string conditionType = "TEXT_EQ")
        {
            int column = headers.ToList().FindIndex(header => string.Equals(header, columnName, StringComparison.OrdinalIgnoreCase));
            if (column < 0)
            {
                return;
            }

            rules.Add(new ManagedConditionalRule
            {
                Column = column,
                ConditionType = conditionType,
                Value = value,
                Red = red,
                Green = green,
                Blue = blue
            });
        }

        private static void AddManagedNumberGreaterRule(
            List<ManagedConditionalRule> rules,
            IReadOnlyList<string> headers,
            string columnName,
            string value,
            float red,
            float green,
            float blue)
        {
            AddManagedTextRule(rules, headers, columnName, value, red, green, blue, "NUMBER_GREATER");
        }

        private static void AddDeleteManagedConditionalRulesRequests(
            List<Request> requests,
            int sheetId,
            IList<ConditionalFormatRule>? existingRules,
            IReadOnlyList<ManagedConditionalRule> managedRules)
        {
            if (existingRules == null || existingRules.Count == 0)
            {
                return;
            }

            for (int i = existingRules.Count - 1; i >= 0; i--)
            {
                if (!managedRules.Any(rule => IsSameConditionalRule(existingRules[i], sheetId, rule)))
                {
                    continue;
                }

                requests.Add(new Request
                {
                    DeleteConditionalFormatRule = new DeleteConditionalFormatRuleRequest
                    {
                        SheetId = sheetId,
                        Index = i
                    }
                });
            }
        }

        private static bool IsSameConditionalRule(ConditionalFormatRule existing, int sheetId, ManagedConditionalRule managed)
        {
            var range = existing.Ranges?.FirstOrDefault();
            var condition = existing.BooleanRule?.Condition;
            string? value = condition?.Values?.FirstOrDefault()?.UserEnteredValue;

            return range?.SheetId == sheetId &&
                   range.StartRowIndex.GetValueOrDefault() == 1 &&
                   range.StartColumnIndex.GetValueOrDefault() == managed.Column &&
                   range.EndColumnIndex.GetValueOrDefault() == managed.Column + 1 &&
                   string.Equals(condition?.Type, managed.ConditionType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(value, managed.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddConditionalRuleRequest(List<Request> requests, int sheetId, ManagedConditionalRule rule)
        {
            requests.Add(new Request
            {
                AddConditionalFormatRule = new AddConditionalFormatRuleRequest
                {
                    Index = 0,
                    Rule = new ConditionalFormatRule
                    {
                        Ranges = new List<GridRange>
                        {
                            new()
                            {
                                SheetId = sheetId,
                                StartRowIndex = 1,
                                StartColumnIndex = rule.Column,
                                EndColumnIndex = rule.Column + 1
                            }
                        },
                        BooleanRule = new BooleanRule
                        {
                            Condition = new BooleanCondition
                            {
                                Type = rule.ConditionType,
                                Values = new List<ConditionValue> { new() { UserEnteredValue = rule.Value } }
                            },
                            Format = new CellFormat
                            {
                                BackgroundColor = new Color { Red = rule.Red, Green = rule.Green, Blue = rule.Blue }
                            }
                        }
                    }
                }
            });
        }

        private bool IsPurchaseSheet(string sheetName)
        {
            string configured = _configService.Config?.GoogleSheets?.PurchaseSheetName ?? "Pembelian";
            return string.Equals(sheetName, configured, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sheetName, "Pembelian", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddManagedChartRequests(
            List<Request> requests,
            int sheetId,
            string sheetName,
            IReadOnlyList<string> headers,
            IList<EmbeddedChart>? existingCharts)
        {
            var charts = BuildManagedCharts(sheetName, headers);
            if (charts.Count == 0)
            {
                return;
            }

            foreach (var chart in charts)
            {
                foreach (var existing in existingCharts ?? Array.Empty<EmbeddedChart>())
                {
                    if (existing.ChartId.HasValue &&
                        string.Equals(existing.Spec?.Title, chart.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        requests.Add(new Request
                        {
                            DeleteEmbeddedObject = new DeleteEmbeddedObjectRequest
                            {
                                ObjectId = existing.ChartId.Value
                            }
                        });
                    }
                }

                requests.Add(new Request
                {
                    AddChart = new AddChartRequest
                    {
                        Chart = BuildEmbeddedChart(sheetId, chart)
                    }
                });
            }
        }

        private static List<ManagedChart> BuildManagedCharts(string sheetName, IReadOnlyList<string> headers)
        {
            var charts = new List<ManagedChart>();
            if (string.Equals(sheetName, "Dashboard", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedChart(charts, headers, "SSA Dashboard - Omzet Profit", "LINE", "Tanggal", new[] { "Omzet", "Profit" }, 1, 12);
                AddManagedChart(charts, headers, "SSA Dashboard - Stock Audit", "COLUMN", "Tanggal", new[] { "Stok Minus", "Stok Habis", "Stok Rendah" }, 18, 12);
            }
            else if (string.Equals(sheetName, "Penjualan_Harian", StringComparison.OrdinalIgnoreCase))
            {
                AddManagedChart(charts, headers, "SSA Sales - Omzet Profit Harian", "LINE", "Tanggal", new[] { "Omzet", "Profit" }, 1, 8);
                AddManagedChart(charts, headers, "SSA Sales - Transaksi Harian", "COLUMN", "Tanggal", new[] { "Jumlah Transaksi", "Items Sold" }, 18, 8);
            }

            return charts;
        }

        private static void AddManagedChart(
            List<ManagedChart> charts,
            IReadOnlyList<string> headers,
            string title,
            string chartType,
            string domainHeader,
            IReadOnlyList<string> seriesHeaders,
            int anchorRow,
            int anchorColumn)
        {
            int domainColumn = FindHeaderIndex(headers, domainHeader);
            var seriesColumns = seriesHeaders
                .Select(header => FindHeaderIndex(headers, header))
                .Where(index => index >= 0)
                .ToList();

            if (domainColumn < 0 || seriesColumns.Count == 0)
            {
                return;
            }

            charts.Add(new ManagedChart
            {
                Title = title,
                ChartType = chartType,
                DomainColumn = domainColumn,
                SeriesColumns = seriesColumns,
                AnchorRow = anchorRow,
                AnchorColumn = anchorColumn
            });
        }

        private static EmbeddedChart BuildEmbeddedChart(int sheetId, ManagedChart chart)
        {
            return new EmbeddedChart
            {
                Spec = new ChartSpec
                {
                    Title = chart.Title,
                    BasicChart = new BasicChartSpec
                    {
                        ChartType = chart.ChartType,
                        LegendPosition = "RIGHT_LEGEND",
                        HeaderCount = 1,
                        Domains = new List<BasicChartDomain>
                        {
                            new()
                            {
                                Domain = BuildChartData(sheetId, chart.DomainColumn)
                            }
                        },
                        Series = chart.SeriesColumns
                            .Select(column => new BasicChartSeries
                            {
                                Series = BuildChartData(sheetId, column),
                                TargetAxis = "LEFT_AXIS"
                            })
                            .ToList(),
                        Axis = new List<BasicChartAxis>
                        {
                            new() { Position = "BOTTOM_AXIS" },
                            new() { Position = "LEFT_AXIS" }
                        }
                    }
                },
                Position = new EmbeddedObjectPosition
                {
                    OverlayPosition = new OverlayPosition
                    {
                        AnchorCell = new GridCoordinate
                        {
                            SheetId = sheetId,
                            RowIndex = chart.AnchorRow,
                            ColumnIndex = chart.AnchorColumn
                        },
                        WidthPixels = 640,
                        HeightPixels = 360
                    }
                }
            };
        }

        private static ChartData BuildChartData(int sheetId, int column)
        {
            return new ChartData
            {
                SourceRange = new ChartSourceRange
                {
                    Sources = new List<GridRange>
                    {
                        new()
                        {
                            SheetId = sheetId,
                            StartRowIndex = 0,
                            StartColumnIndex = column,
                            EndColumnIndex = column + 1
                        }
                    }
                }
            };
        }

        private static int FindHeaderIndex(IReadOnlyList<string> headers, string headerName)
        {
            return headers.ToList().FindIndex(header => string.Equals(header, headerName, StringComparison.OrdinalIgnoreCase));
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

        private static string BuildPurchaseRowKey(
            string? documentNumber,
            int lineIndex,
            int productId,
            decimal quantity,
            decimal total)
        {
            return BuildKey(new object?[]
            {
                documentNumber,
                lineIndex.ToString("D4"),
                productId,
                quantity,
                total
            });
        }

        private static string BuildPurchaseCorrelationId(string? documentNumber, DateTime? receiptDate, int lineIndex)
        {
            string doc = NormalizeKeyValue(documentNumber);
            if (string.IsNullOrWhiteSpace(doc))
            {
                doc = "no-doc";
            }

            return BuildKey(new object?[]
            {
                "purchase",
                doc,
                (receiptDate ?? DateTime.Now).ToString("yyyyMMdd"),
                lineIndex.ToString("D4")
            });
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
