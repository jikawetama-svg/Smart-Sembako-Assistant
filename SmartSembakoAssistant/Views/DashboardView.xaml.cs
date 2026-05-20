using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private readonly BotController? _botController;
        private DispatcherTimer? _autoRefreshTimer;

        public DashboardView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService,
            BotController? botController)
        {
            InitializeComponent();

            _configService = configService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
            _botController = botController;

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
                var integration = _botController?.GetIntegrationStatus();
                if (_botController != null && _botController.IsRunning)
                {
                    bool anyPrimary = integration?.TelegramRunning == true || integration?.WhatsAppRunning == true || integration?.BaileysRunning == true;
                    TxtBotStatus.Text = anyPrimary ? "Runtime On" : "Runtime Partial";
                    TxtBotStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
                    TxtBotUptime.Text = $"TG: {(integration?.TelegramRunning == true ? "On" : "Off")} | WA Cloud: {(integration?.WhatsAppRunning == true ? "On" : "Off")} | Baileys: {(integration?.BaileysRunning == true ? "On" : "Off")} | Tunnel: {(integration?.TunnelRunning == true ? "On" : "Off")}";
                }
                else
                {
                    TxtBotStatus.Text = "Runtime Off";
                    TxtBotStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                    TxtBotUptime.Text = "WA Cloud: Off | Baileys: Off | Tunnel: Off";
                }

                TxtIntegrationSummary.Text = integration == null
                    ? "Telegram, WhatsApp, webhook, dan outbox belum diinisialisasi."
                    : $"Mode WA: {integration.WhatsAppMode} | WA webhook: {(integration.WhatsAppRunning ? "aktif" : "mati")} | Cloud outbound: {(integration.WhatsAppCloudOutboundReady ? "siap kirim" : "belum siap")} | Baileys: {(integration.BaileysOutboundReady ? "siap kirim" : integration.BaileysConfigured ? "terputus/menunggu pairing" : "mati")} | Pairing: {(integration.BaileysPaired ? "terhubung" : integration.BaileysConfigured ? "menunggu pairing" : "-")} | Signature: {(integration.SignatureValidationEnabled ? "aktif" : "local/test")} | Outbox: {integration.PendingOutboundCount} | Last webhook: {FormatTimestamp(integration.LastWebhookReceivedAt)} | Last sent: {FormatTimestamp(integration.LastOutboundSentAt)}";

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
                    TxtDbPath.Text = $"Outbox pending: {integration?.PendingOutboundCount ?? 0}";
                }
                else
                {
                    TxtDbStatus.Text = "Not Connected";
                    TxtDbStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
                }

                // Memory stats
                var conversations = await _databaseService.GetRecentConversationsAsync(null, 1000);
                TxtMemoryCount.Text = $"{conversations.Count} conversations";

                string dbPath = RuntimePaths.MemoryDatabasePath;
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

        private static string FormatTimestamp(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd/MM HH:mm:ss") : "-";
        }
    }
}
