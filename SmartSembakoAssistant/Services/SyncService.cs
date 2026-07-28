using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class SyncService : IDisposable
    {
        private readonly PosDbService _posDbService;
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly DispatcherTimer _timer;

        private DateTime _lastSyncTime = DateTime.MinValue;
        private bool _isSyncing;
        private bool _disposed;

        public event EventHandler? SyncStateChanged;

        public bool IsRunning => _timer.IsEnabled;
        public bool IsSyncing => _isSyncing;
        public DateTime? LastSyncTime => _lastSyncTime == DateTime.MinValue ? null : _lastSyncTime;
        public int LastSyncedCount { get; private set; }
        public string LastSyncStatus { get; private set; } = "Belum Diinisialisasi";
        public long LastSyncDurationMs { get; private set; }

        public SyncService(
            PosDbService posDbService,
            ConfigService configService,
            LoggingService loggingService)
        {
            _posDbService = posDbService ?? throw new ArgumentNullException(nameof(posDbService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            int intervalMinutes = _configService.Config?.Supabase?.SyncIntervalMinutes ?? 15;
            if (intervalMinutes <= 0) intervalMinutes = 15;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMinutes)
            };
            _timer.Tick += async (s, e) => await SyncDeltaAsync();
        }

        public void Start()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                LastSyncStatus = "Background Sync Aktif";
                NotifyStateChanged();

                // Run initial sync in background
                _ = SyncDeltaAsync();
            }
        }

        public void Stop()
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
                LastSyncStatus = "Sync Dihentikan";
                NotifyStateChanged();
            }
        }

        public async Task<bool> SyncDeltaAsync()
        {
            if (_isSyncing) return false;

            var supabaseConfig = _configService.Config?.Supabase;
            if (supabaseConfig == null || !supabaseConfig.Enabled)
            {
                LastSyncStatus = "Cloud Sync Nonaktif";
                NotifyStateChanged();
                return false;
            }
            if (string.IsNullOrWhiteSpace(supabaseConfig.MerchantId))
            {
                LastSyncStatus = "Sync ditolak: MerchantId belum tersedia. Buka ulang aplikasi agar dibuat otomatis.";
                await _loggingService.LogWarningAsync(LastSyncStatus, "SyncService");
                NotifyStateChanged();
                return false;
            }
            if (string.Equals(supabaseConfig.SyncMode, "read_only", StringComparison.OrdinalIgnoreCase))
            {
                LastSyncStatus = "Sync write dilewati: perangkat ini read_only";
                NotifyStateChanged();
                return true;
            }

            _isSyncing = true;
            LastSyncStatus = "Sedang Menyinkronkan...";
            NotifyStateChanged();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var products = await _posDbService.GetAllProductsAsync();
                
                var deltaProducts = products.Where(p => p.IsActive).ToList();

                if (deltaProducts.Any())
                {
                    var dtos = deltaProducts.Select(p => new ProductSyncDTO
                    {
                        // ID cloud bersifat global; ID produk POS asli tetap disimpan untuk audit.
                        Id = $"{supabaseConfig.MerchantId}:{p.Id}",
                        MerchantId = supabaseConfig.MerchantId ?? string.Empty,
                        SourceDeviceId = supabaseConfig.DeviceId,
                        SourceProductId = p.Id,
                        Name = p.Name ?? string.Empty,
                        Stock = p.Stock ?? 0,
                        Unit = p.Unit ?? "pcs",
                        SellingPrice = p.SellingPrice ?? 0,
                        IsLowStock = (p.Stock ?? 0) <= 10,
                        CategoryName = p.Category,
                        Barcode = p.Sku,
                        SyncedAt = DateTime.UtcNow
                    }).ToList();

                    using var supabaseClient = new SupabaseClient(_configService);
                    (bool success, int count, string? err) = await supabaseClient.UpsertProductsAsync(dtos);

                    if (!success)
                    {
                        LastSyncStatus = $"Gagal: {err}";
                        await _loggingService.LogErrorAsync($"Supabase Delta Sync Gagal: {err}", "SyncService");
                        return false;
                    }

                    // Sync daily transaction summary
                    try
                    {
                        decimal todayRevenue = await _posDbService.GetTodayRevenueAsync();
                        decimal todayProfit = await _posDbService.GetTodayProfitAsync();
                        int todayTransactionCount = await _posDbService.GetSalesTransactionCountAsync(
                            DateTime.Today,
                            DateTime.Today);

                        var summaryDTO = new TransactionSummaryDTO
                        {
                            Id = $"{supabaseConfig.MerchantId}:{DateTime.UtcNow:yyyy-MM-dd}",
                            MerchantId = supabaseConfig.MerchantId ?? string.Empty,
                            SourceDeviceId = supabaseConfig.DeviceId,
                            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                            TotalRevenue = todayRevenue,
                            TotalProfit = todayProfit,
                            TotalTransactions = todayTransactionCount,
                            SyncedAt = DateTime.UtcNow
                        };

                        await supabaseClient.UpsertTransactionSummaryAsync(summaryDTO);
                        await supabaseClient.UpdateSyncMetadataAsync("last_delta_sync", DateTime.UtcNow.ToString("o"));
                    }
                    catch (Exception exSummary)
                    {
                        await _loggingService.LogWarningAsync($"Gagal update summary transaksi Supabase: {exSummary.Message}", "SyncService");
                    }

                    try
                    {
                        var receivables = await _posDbService.GetCustomerReceivablesAsync();
                        var debtByCustomerId = receivables
                            .Where(item => !string.IsNullOrWhiteSpace(item.CustomerId))
                            .GroupBy(item => item.CustomerId!)
                            .ToDictionary(
                                group => group.Key,
                                group => new
                                {
                                    TotalDebt = group.Sum(item => item.TotalOwed),
                                    LastTransactionDate = group.Max(item => item.LastTransactionDate)
                                });

                        var customers = await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true);
                        var customerDtos = customers
                            .Where(customer => !string.IsNullOrWhiteSpace(customer.Id) && !string.IsNullOrWhiteSpace(customer.Name))
                            .Select(customer =>
                            {
                                debtByCustomerId.TryGetValue(customer.Id!, out var debt);
                                return new CustomerSyncDTO
                                {
                                    Id = $"{supabaseConfig.MerchantId}:{customer.Id}",
                                    MerchantId = supabaseConfig.MerchantId ?? string.Empty,
                                    SourceDeviceId = supabaseConfig.DeviceId,
                                    Name = customer.Name ?? string.Empty,
                                    Phone = customer.Phone,
                                    TotalDebt = debt?.TotalDebt ?? 0,
                                    LastTransactionDate = debt?.LastTransactionDate ?? customer.LastPurchaseDate,
                                    SyncedAt = DateTime.UtcNow
                                };
                            })
                            .ToList();

                        var customerSync = await supabaseClient.UpsertCustomersAsync(customerDtos);
                        if (!customerSync.success)
                        {
                            await _loggingService.LogWarningAsync($"Gagal sync pelanggan Supabase: {customerSync.error}", "SyncService");
                        }

                        var suppliers = await _posDbService.GetSuppliersAsync(null, null);
                        var supplierDtos = suppliers
                            .Where(supplier => !string.IsNullOrWhiteSpace(supplier.Id) && !string.IsNullOrWhiteSpace(supplier.Name))
                            .Select(supplier => new SupplierSyncDTO
                            {
                                Id = $"{supabaseConfig.MerchantId}:{supplier.Id}",
                                MerchantId = supabaseConfig.MerchantId ?? string.Empty,
                                SourceDeviceId = supabaseConfig.DeviceId,
                                Name = supplier.Name ?? string.Empty,
                                Phone = supplier.Phone,
                                Email = supplier.Email,
                                SyncedAt = DateTime.UtcNow
                            })
                            .ToList();

                        var supplierSync = await supabaseClient.UpsertSuppliersAsync(supplierDtos);
                        if (!supplierSync.success)
                        {
                            await _loggingService.LogWarningAsync($"Gagal sync supplier Supabase: {supplierSync.error}", "SyncService");
                        }
                    }
                    catch (Exception exDirectorySync)
                    {
                        await _loggingService.LogWarningAsync($"Gagal sync pelanggan/supplier Supabase: {exDirectorySync.Message}", "SyncService");
                    }

                    _lastSyncTime = DateTime.UtcNow;
                    LastSyncedCount = count;
                    stopwatch.Stop();
                    LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
                    LastSyncStatus = $"Sukses ({count} produk, {LastSyncDurationMs}ms)";

                    await _loggingService.LogInfoAsync("SyncService", $"Supabase Delta Sync berhasil: {count} produk disinkronkan dalam {LastSyncDurationMs}ms.");
                    return true;
                }
                else
                {
                    stopwatch.Stop();
                    LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
                    LastSyncStatus = "Data Sudah Sinkron (0 perubahan)";
                    return true;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
                LastSyncStatus = $"Error: {ex.Message}";
                await _loggingService.LogErrorAsync($"Error pada SyncDeltaAsync: {ex.Message}", "SyncService");
                return false;
            }
            finally
            {
                _isSyncing = false;
                NotifyStateChanged();
            }
        }

        private void NotifyStateChanged()
        {
            SyncStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer.Stop();
                }
                _disposed = true;
            }
        }
    }
}
