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
                        Id = p.Id ?? string.Empty,
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

                        var summaryDTO = new TransactionSummaryDTO
                        {
                            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                            TotalRevenue = todayRevenue,
                            TotalProfit = todayProfit,
                            TotalTransactions = 0,
                            SyncedAt = DateTime.UtcNow
                        };

                        await supabaseClient.UpsertTransactionSummaryAsync(summaryDTO);
                        await supabaseClient.UpdateSyncMetadataAsync("last_delta_sync", DateTime.UtcNow.ToString("o"));
                    }
                    catch (Exception exSummary)
                    {
                        await _loggingService.LogWarningAsync($"Gagal update summary transaksi Supabase: {exSummary.Message}", "SyncService");
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
