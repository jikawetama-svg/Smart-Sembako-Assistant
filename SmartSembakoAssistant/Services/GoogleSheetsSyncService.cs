using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class GoogleSheetsSyncService
    {
        private static readonly IReadOnlyList<string> DashboardHeaders = new[]
        {
            "Tanggal",
            "Omzet",
            "Profit",
            "Jumlah Transaksi",
            "Rata-rata Transaksi",
            "Stok Minus",
            "Stok Habis",
            "Stok Rendah",
            "Total Piutang",
            "Produk Tanpa Modal",
            "Produk Harga Jual 0/Minus",
            "SyncedAt"
        };

        private static readonly IReadOnlyList<string> CriticalStockHeaders = new[]
        {
            "Kode",
            "Nama",
            "Stok",
            "Satuan",
            "Harga Beli",
            "Harga Jual",
            "Nilai Stok",
            "Kategori",
            "Status Audit",
            "SyncedAt"
        };

        private static readonly IReadOnlyList<string> ProductAuditHeaders = new[]
        {
            "Kode",
            "Nama",
            "Stok",
            "Satuan",
            "Harga Beli",
            "Harga Jual",
            "Kategori",
            "Audit Flags",
            "SyncedAt"
        };

        private static readonly IReadOnlyList<string> DailySalesHeaders = new[]
        {
            "Tanggal",
            "Omzet",
            "Profit",
            "Jumlah Transaksi",
            "Items Sold",
            "Margin %",
            "SyncedAt"
        };

        private static readonly IReadOnlyList<string> ReceivableHeaders = new[]
        {
            "CustomerId",
            "Pelanggan",
            "Telepon",
            "Jumlah Invoice",
            "Total Piutang",
            "Jatuh Tempo Tertua",
            "Transaksi Terakhir",
            "Status",
            "SyncedAt"
        };

        private static readonly IReadOnlyList<string> PurchaseHeaders = GoogleSheetsSchema.PurchaseHeaders;

        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly GoogleSheetsService _googleSheetsService;
        private readonly PosDbService? _posDbService;

        public GoogleSheetsSyncService(
            ConfigService configService,
            LoggingService loggingService,
            GoogleSheetsService googleSheetsService,
            PosDbService? posDbService)
        {
            _configService = configService;
            _loggingService = loggingService;
            _googleSheetsService = googleSheetsService;
            _posDbService = posDbService;
        }

        public async Task<(bool Success, string Message)> PreparePrioritySheetsAsync()
        {
            try
            {
                EnsureEnabled();
                await _googleSheetsService.EnsureSheetWithHeaderAsync("Dashboard", DashboardHeaders);
                await _googleSheetsService.EnsureSheetWithHeaderAsync("Stok_Kritis", CriticalStockHeaders);
                await _googleSheetsService.EnsureSheetWithHeaderAsync("Produk_Audit", ProductAuditHeaders);
                await _googleSheetsService.EnsureSheetWithHeaderAsync("Penjualan_Harian", DailySalesHeaders);
                await _googleSheetsService.EnsureSheetWithHeaderAsync("Piutang", ReceivableHeaders);
                await SyncPurchaseHeadersAsync();

                await _loggingService.LogInfoAsync("Google Sheets priority tabs prepared.", "GoogleSheets");
                return (true, "Tab prioritas Google Sheets siap: Dashboard, Stok_Kritis, Penjualan_Harian, Piutang, Pembelian.");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Prepare Google Sheets gagal: {ex.Message}", "GoogleSheets", ex.ToString());
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string Message)> SyncDailySnapshotAsync(DateTime date)
        {
            try
            {
                EnsureEnabled();
                await SyncDashboardAsync(date);
                await SyncCriticalStockAsync();
                await SyncProductAuditAsync();
                await SyncDailySalesAsync(date.Date, date.Date);
                await SyncReceivablesAsync();

                await _loggingService.LogInfoAsync($"Google Sheets daily snapshot synced for {date:yyyy-MM-dd}.", "GoogleSheets");
                return (true, $"Daily snapshot Google Sheets tersinkron untuk {date:yyyy-MM-dd}.");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Daily snapshot Google Sheets gagal: {ex.Message}", "GoogleSheets", ex.ToString());
                return (false, ex.Message);
            }
        }

        public async Task SyncDashboardAsync(DateTime date)
        {
            EnsureEnabled();
            var posDbService = EnsurePosDb();
            DateTime day = date.Date;
            decimal omzet = await posDbService.GetSalesRevenueAsync(day, day);
            decimal profit = await posDbService.GetSalesProfitAsync(day, day);
            int transactionCount = await posDbService.GetSalesTransactionCountAsync(day, day);
            var products = await posDbService.GetAllProductsAsync();
            decimal totalReceivable = await posDbService.GetTotalReceivableAsync();
            int noCostCount = (await posDbService.GetNoCostProductsForExportAsync(includeAllZeroCostProducts: true)).Count;
            string syncedAt = NowString();

            var row = new Dictionary<string, object>
            {
                ["Tanggal"] = DateString(day),
                ["Omzet"] = omzet,
                ["Profit"] = profit,
                ["Jumlah Transaksi"] = transactionCount,
                ["Rata-rata Transaksi"] = transactionCount > 0 ? Math.Round(omzet / transactionCount, 2) : 0,
                ["Stok Minus"] = products.Count(product => product.Stock.GetValueOrDefault() < 0),
                ["Stok Habis"] = products.Count(product => product.Stock.GetValueOrDefault() == 0),
                ["Stok Rendah"] = products.Count(product => product.Stock.GetValueOrDefault() > 0 && product.Stock.GetValueOrDefault() <= 10),
                ["Total Piutang"] = totalReceivable,
                ["Produk Tanpa Modal"] = noCostCount,
                ["Produk Harga Jual 0/Minus"] = products.Count(product => product.SellingPrice.GetValueOrDefault() <= 0),
                ["SyncedAt"] = syncedAt
            };

            await _googleSheetsService.UpsertRowsAsync("Dashboard", DashboardHeaders, new[] { "Tanggal" }, new[] { row });
        }

        public async Task SyncCriticalStockAsync()
        {
            EnsureEnabled();
            var posDbService = EnsurePosDb();
            var products = (await posDbService.GetAllProductsAsync())
                .Where(product => product.Stock.GetValueOrDefault() <= 10)
                .OrderBy(product => product.Stock.GetValueOrDefault())
                .ThenBy(product => product.Name)
                .ToList();

            if (!products.Any())
            {
                products = await posDbService.GetCriticalStockProductsAsync();
            }

            string syncedAt = NowString();
            var rows = products.Select(product => (IReadOnlyList<object>)new List<object>
            {
                product.Sku ?? string.Empty,
                product.Name ?? string.Empty,
                product.Stock ?? 0,
                product.Unit ?? "Pcs",
                product.PurchasePrice ?? 0,
                product.SellingPrice ?? 0,
                (product.Stock ?? 0) * (product.PurchasePrice ?? 0),
                product.Category ?? string.Empty,
                GetStockStatus(product.Stock),
                syncedAt
            }).ToList();

            await _googleSheetsService.ReplaceSheetRowsAsync("Stok_Kritis", CriticalStockHeaders, rows);
        }

        public async Task SyncProductAuditAsync()
        {
            EnsureEnabled();
            var posDbService = EnsurePosDb();
            var products = (await posDbService.GetAllProductsAsync())
                .Select(product => new { Product = product, Flags = GetProductAuditFlags(product) })
                .Where(item => item.Flags.Any())
                .OrderBy(item => item.Product.Name)
                .ToList();

            string syncedAt = NowString();
            var rows = products.Select(item => (IReadOnlyList<object>)new List<object>
            {
                item.Product.Sku ?? string.Empty,
                item.Product.Name ?? string.Empty,
                item.Product.Stock ?? 0,
                item.Product.Unit ?? "Pcs",
                item.Product.PurchasePrice ?? 0,
                item.Product.SellingPrice ?? 0,
                item.Product.Category ?? string.Empty,
                string.Join("|", item.Flags),
                syncedAt
            }).ToList();

            await _googleSheetsService.ReplaceSheetRowsAsync("Produk_Audit", ProductAuditHeaders, rows);
        }

        public async Task SyncDailySalesAsync(DateTime startDate, DateTime endDate)
        {
            EnsureEnabled();
            var posDbService = EnsurePosDb();
            var rows = new List<IReadOnlyDictionary<string, object>>();
            for (DateTime day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                decimal omzet = await posDbService.GetSalesRevenueAsync(day, day);
                decimal profit = await posDbService.GetSalesProfitAsync(day, day);
                int transactionCount = await posDbService.GetSalesTransactionCountAsync(day, day);
                decimal itemsSold = (await posDbService.GetSalesLineItemsAsync(day, day)).Sum(item => item.Quantity);

                rows.Add(new Dictionary<string, object>
                {
                    ["Tanggal"] = DateString(day),
                    ["Omzet"] = omzet,
                    ["Profit"] = profit,
                    ["Jumlah Transaksi"] = transactionCount,
                    ["Items Sold"] = itemsSold,
                    ["Margin %"] = omzet > 0 ? Math.Round(profit / omzet, 4) : 0,
                    ["SyncedAt"] = NowString()
                });
            }

            await _googleSheetsService.UpsertRowsAsync("Penjualan_Harian", DailySalesHeaders, new[] { "Tanggal" }, rows);
        }

        public async Task SyncReceivablesAsync()
        {
            EnsureEnabled();
            var posDbService = EnsurePosDb();
            var receivables = await posDbService.GetCustomerReceivablesAsync();
            string syncedAt = NowString();
            var rows = receivables.Select(item => (IReadOnlyList<object>)new List<object>
            {
                item.CustomerId ?? string.Empty,
                item.CustomerName,
                item.Phone ?? string.Empty,
                item.InvoiceCount,
                item.TotalOwed,
                item.OldestDueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                item.LastTransactionDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                GetReceivableStatus(item),
                syncedAt
            }).ToList();

            await _googleSheetsService.ReplaceSheetRowsAsync("Piutang", ReceivableHeaders, rows);
        }

        public async Task SyncPurchaseHeadersAsync()
        {
            string sheetName = _configService.Config?.GoogleSheets?.PurchaseSheetName ?? "Pembelian";
            await _googleSheetsService.EnsureSheetWithHeaderAsync(sheetName, PurchaseHeaders);
        }

        private void EnsureEnabled()
        {
            if (_configService.Config?.GoogleSheets?.Enabled != true)
            {
                throw new InvalidOperationException("Google Sheets belum diaktifkan di Settings.");
            }
        }

        private PosDbService EnsurePosDb()
        {
            return _posDbService ?? throw new InvalidOperationException("Database pos.db belum siap.");
        }

        private static string DateString(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private static string NowString()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string GetStockStatus(decimal? stock)
        {
            decimal value = stock.GetValueOrDefault();
            if (value < 0) return "MINUS";
            if (value == 0) return "HABIS";
            if (value <= 10) return "RENDAH";
            return "AMAN";
        }

        private static string GetStockRecommendation(decimal? stock)
        {
            decimal value = stock.GetValueOrDefault();
            if (value <= 0) return "Restock segera";
            if (value <= 5) return "Prioritas tinggi";
            return "Cek kebutuhan";
        }

        private static string GetReceivableStatus(CustomerReceivable receivable)
        {
            if (receivable.OldestDueDate.HasValue && receivable.OldestDueDate.Value.Date < DateTime.Today)
            {
                return "OVERDUE";
            }

            if (receivable.OldestDueDate.HasValue && receivable.OldestDueDate.Value.Date <= DateTime.Today.AddDays(7))
            {
                return "DUE_SOON";
            }

            return "OK";
        }

        private static List<string> GetProductAuditFlags(Product product)
        {
            var flags = new List<string>();
            if (product.Stock.GetValueOrDefault() < 0) flags.Add("STOK_MINUS");
            if (product.Stock.GetValueOrDefault() == 0) flags.Add("STOK_0");
            if (product.PurchasePrice.GetValueOrDefault() == 0) flags.Add("COST_0");
            if (product.PurchasePrice.GetValueOrDefault() < 0) flags.Add("COST_MINUS");
            if (product.SellingPrice.GetValueOrDefault() == 0) flags.Add("HARGA_JUAL_0");
            if (product.SellingPrice.GetValueOrDefault() < 0) flags.Add("HARGA_JUAL_MINUS");
            if (string.IsNullOrWhiteSpace(product.Category)) flags.Add("KATEGORI_KOSONG");
            return flags;
        }
    }
}
