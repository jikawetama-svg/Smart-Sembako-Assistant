using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private readonly TelegramBotService? _botService;
        private DispatcherTimer? _autoRefreshTimer;

        public DashboardView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService,
            TelegramBotService? botService)
        {
            InitializeComponent();

            _configService = configService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
            _botService = botService;

            LoadDashboardData();
            SetupAutoRefresh();
        }

        private void SetupAutoRefresh()
        {
            _autoRefreshTimer = new DispatcherTimer();
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(30);
            _autoRefreshTimer.Tick += async (s, e) =>
            {
                await RefreshDataAsync();
            };
            _autoRefreshTimer.Start();
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                if (_posDbService != null)
                {
                    var revenue = await _posDbService.GetTodayRevenueAsync();
                    var profit = await _posDbService.GetTodayProfitAsync();
                    var lowStock = await _posDbService.GetLowStockProductsAsync(5);

                    TxtRevenue.Text = $"Rp {revenue:N0}";
                    TxtProfit.Text = $"Rp {profit:N0}";
                    TxtCriticalStock.Text = $"{lowStock.Count} produk";
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Auto-refresh error: {ex.Message}", "Dashboard");
            }
        }

        private async void LoadDashboardData()
        {
            try
            {
                // Bot status
                if (_botService != null && _botService.IsRunning)
                {
                    TxtBotStatus.Text = "Running";
                    TxtBotStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
                }
                else
                {
                    TxtBotStatus.Text = "Stopped";
                    TxtBotStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                }

                // Groq status - TEST API CALL dulu sebelum tampilkan "Connected"
                var config = _configService.Config;
                if (config?.Groq?.ApiKey != null && config.Groq.ApiKey != "YOUR_GROQ_API_KEY")
                {
                    try
                    {
                        // Test API call untuk verify connection
                        var groqService = new GroqService(_configService, _loggingService);
                        var (success, message) = await groqService.TestGroqConnectionAsync();
                        
                        if (success)
                        {
                            TxtGroqStatus.Text = "Connected";
                            TxtGroqStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
                            TxtGroqModel.Text = $"Model: {config.Groq.Model ?? "llama-3.1-8b-instant"}";
                        }
                        else
                        {
                            TxtGroqStatus.Text = "Error";
                            TxtGroqStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#F59E0B")!;
                            TxtGroqModel.Text = message;
                        }
                    }
                    catch (Exception ex)
                    {
                        TxtGroqStatus.Text = "Error";
                        TxtGroqStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                        TxtGroqModel.Text = $"Connection failed: {ex.Message}";
                    }
                }
                else
                {
                    TxtGroqStatus.Text = "Not Configured";
                    TxtGroqStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                    TxtGroqModel.Text = "Setup di Settings";
                }

                // Database status
                if (_posDbService != null)
                {
                    TxtDbStatus.Text = "Connected";
                    TxtDbStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
                    TxtDbPath.Text = "Path: pos.db";
                }
                else
                {
                    TxtDbStatus.Text = "Not Connected";
                    TxtDbStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                }

                // Memory stats
                var conversations = await _databaseService.GetRecentConversationsAsync(null, 1000);
                TxtMemoryCount.Text = $"{conversations.Count} conversations";

                string dbPath = "data\\memory.db";
                if (System.IO.File.Exists(dbPath))
                {
                    long sizeInBytes = new System.IO.FileInfo(dbPath).Length;
                    double sizeInKB = sizeInBytes / 1024.0;
                    TxtMemorySize.Text = $"DB Size: {sizeInKB:F0} KB";
                }

                // Revenue and profit
                if (_posDbService != null)
                {
                    var revenue = await _posDbService.GetTodayRevenueAsync();
                    var profit = await _posDbService.GetTodayProfitAsync();
                    var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
                    var avgTransactions = await _posDbService.GetAverageDailyTransactionsAsync();
                    
                    TxtRevenue.Text = $"Rp {revenue:N0}";
                    TxtProfit.Text = $"Rp {profit:N0}";
                    
                    // vs kemarin
                    if (yesterdayRevenue > 0)
                    {
                        var diff = revenue - yesterdayRevenue;
                        var percent = (diff / yesterdayRevenue) * 100;
                        var sign = diff >= 0 ? "↑" : "↓";
                        TxtRevenueVsYesterday.Text = $"vs kemarin: Rp {yesterdayRevenue:N0} ({sign}{Math.Abs(percent):F1}%)";
                        TxtRevenueVsYesterday.Foreground = diff >= 0 
                            ? (Brush)new BrushConverter().ConvertFrom("#10B981")!
                            : (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                    }
                    else
                    {
                        TxtRevenueVsYesterday.Text = "vs kemarin: Rp 0";
                    }
                    
                    // Profit margin
                    if (revenue > 0)
                    {
                        var margin = (profit / revenue) * 100;
                        TxtProfitMargin.Text = $"Margin: {margin:F1}% | Avg: {avgTransactions:F1} transaksi/hari";
                    }
                    else
                    {
                        TxtProfitMargin.Text = "Margin: -";
                    }

                    var lowStock = await _posDbService.GetLowStockProductsAsync(5);
                    TxtCriticalStock.Text = $"{lowStock.Count} produk";
                }

                // Recent conversations
                var recentChats = await _databaseService.GetRecentConversationsAsync(0, 5);
                LstRecentChats.ItemsSource = recentChats;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error loading dashboard: {ex.Message}", "Dashboard", ex.ToString());
            }
        }

        /// <summary>
        /// Public method to reload data (called from MainWindow sync)
        /// </summary>
        public async Task LoadDataAsync()
        {
            LoadDashboardData();
        }
    }
}
