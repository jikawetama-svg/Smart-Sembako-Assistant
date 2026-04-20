using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartSembakoAssistant.Services;
using SmartSembakoAssistant.Views;

namespace SmartSembakoAssistant
{
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private PosDbService? _posDbService;
        private BotController? _botController;
        private DispatcherTimer? _uptimeTimer;
        private DispatcherTimer? _dateTimeTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _configService = new ConfigService();
            _databaseService = new DatabaseService();
            _loggingService = new LoggingService(_databaseService);

            // Initialize PosDbService
            InitializePosDbService();

            // Initialize BotController
            _botController = new BotController(
                _configService,
                _databaseService,
                _loggingService,
                _posDbService);

            // Subscribe to bot state changes
            _botController.OnStateChanged += BotController_OnStateChanged;

            // Setup Timers
            SetupUptimeTimer();
            SetupDateTimeTimer();

            // Load dashboard
            LoadDashboard();
        }

        private void InitializePosDbService()
        {
            try
            {
                string? posDbPath = PosDbService.AutoDetectPosDbPath();
                if (!string.IsNullOrEmpty(posDbPath))
                {
                    _posDbService = new PosDbService(posDbPath, _loggingService);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Error initializing PosDbService: {ex.Message}", "System");
            }
        }

        private void SetupUptimeTimer()
        {
            _uptimeTimer = new DispatcherTimer();
            _uptimeTimer.Interval = TimeSpan.FromSeconds(1);
            _uptimeTimer.Tick += (s, e) =>
            {
                if (_botController != null && _botController.IsRunning)
                {
                    var uptime = _botController.Uptime;
                    if (uptime != null)
                    {
                        TxtBotUptime.Text = $"Uptime: {uptime.Value.Hours}h {uptime.Value.Minutes}m";
                        TxtBotUptime.Visibility = Visibility.Visible;
                    }
                }
            };
            _uptimeTimer.Start();
        }

        private void SetupDateTimeTimer()
        {
            _dateTimeTimer = new DispatcherTimer();
            _dateTimeTimer.Interval = TimeSpan.FromSeconds(1);
            _dateTimeTimer.Tick += (s, e) =>
            {
                TxtDateTime.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            };
            _dateTimeTimer.Start();
        }

        private void BtnDrawerToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleDrawerInternal();
        }

        /// <summary>
        /// Public method untuk toggle drawer dari Views
        /// </summary>
        public void ToggleDrawer()
        {
            ToggleDrawerInternal();
        }

        private void ToggleDrawerInternal()
        {
            if (DrawerColumn.Width.Value > 0)
            {
                DrawerColumn.Width = new GridLength(0);
                BtnDrawerToggle.Content = "☰";
            }
            else
            {
                DrawerColumn.Width = new GridLength(220);
                BtnDrawerToggle.Content = "✕";
            }
        }

        private void BotController_OnStateChanged(object? sender, BotState state)
        {
            Dispatcher.Invoke(() => UpdateBotUI(state));
        }

        private void UpdateBotUI(BotState state)
        {
            switch (state)
            {
                case BotState.Running:
                    TxtBotStatus.Text = "🟢 Bot Aktif";
                    TxtBotStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                    TxtBotUptime.Visibility = Visibility.Visible;
                    BtnStartBot.IsEnabled = false;
                    BtnStopBot.IsEnabled = true;
                    BtnRestartBot.IsEnabled = true;
                    break;

                case BotState.Stopped:
                    TxtBotStatus.Text = "🔴 Bot Stop";
                    TxtBotStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    TxtBotUptime.Visibility = Visibility.Collapsed;
                    TxtBotUptime.Text = "Uptime: -";
                    BtnStartBot.IsEnabled = true;
                    BtnStopBot.IsEnabled = false;
                    BtnRestartBot.IsEnabled = false;
                    break;

                case BotState.Starting:
                    TxtBotStatus.Text = "🔄 Starting...";
                    TxtBotStatus.Foreground = System.Windows.Media.Brushes.Yellow;
                    BtnStartBot.IsEnabled = false;
                    BtnStopBot.IsEnabled = false;
                    BtnRestartBot.IsEnabled = false;
                    break;

                case BotState.Stopping:
                    TxtBotStatus.Text = "🔄 Stopping...";
                    TxtBotStatus.Foreground = System.Windows.Media.Brushes.Yellow;
                    BtnStartBot.IsEnabled = false;
                    BtnStopBot.IsEnabled = false;
                    BtnRestartBot.IsEnabled = false;
                    break;

                case BotState.Error:
                    TxtBotStatus.Text = "⚠️ Bot Error";
                    TxtBotStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    BtnStartBot.IsEnabled = true;
                    BtnStopBot.IsEnabled = false;
                    BtnRestartBot.IsEnabled = true;
                    break;
            }
        }

        // Navigation Handlers
        private void BtnDashboard_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Dashboard");
            SetActiveButton(BtnDashboard);
            LoadDashboard();
        }

        private void BtnMonitoring_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Stock Monitoring");
            SetActiveButton(BtnMonitoring);
            LoadMonitoring();
        }

        private void BtnAnalytics_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Sales Analytics");
            SetActiveButton(BtnAnalytics);
            LoadAnalytics();
        }

        private void BtnReports_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Reports & Analytics");
            SetActiveButton(BtnReports);
            LoadReports();
        }

        private void BtnAIChat_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("AI Chat Assistant");
            SetActiveButton(BtnAIChat);
            LoadAIChat();
        }

        private void BtnLogs_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Activity Logs");
            SetActiveButton(BtnLogs);
            LoadLogs();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            UpdatePageTitle("Settings");
            SetActiveButton(BtnSettings);
            LoadSettings();
        }

        private void SetActiveButton(Button activeButton)
        {
            // Reset all buttons
            BtnDashboard.Tag = null;
            BtnMonitoring.Tag = null;
            BtnAnalytics.Tag = null;
            BtnReports.Tag = null;
            BtnAIChat.Tag = null;
            BtnLogs.Tag = null;
            BtnSettings.Tag = null;

            // Set active
            activeButton.Tag = "Active";
            
            // Auto-hide sidebar after menu click
            DrawerColumn.Width = new GridLength(0);
            BtnDrawerToggle.Content = "☰";
        }

        private void UpdatePageTitle(string title)
        {
            TxtPageTitle.Text = title;
        }

        // Bot Control Handlers
        private async void BtnStartBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnStartBot.IsEnabled = false;
                bool started = await _botController!.StartAsync();
                
                if (!started)
                {
                    MessageBox.Show("❌ Gagal memulai bot. Periksa konfigurasi Anda.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStopBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnStopBot.IsEnabled = false;
                await _botController!.StopAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRestartBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("🔄 Restart bot? Bot akan stop lalu start kembali.", 
                    "Confirm Restart", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    BtnRestartBot.IsEnabled = false;
                    bool restarted = await _botController!.RestartAsync();
                    
                    if (!restarted)
                    {
                        MessageBox.Show("❌ Gagal restart bot.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Quick Actions Handlers
        private async void QuickAction_Omzet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_posDbService == null)
                {
                    MessageBox.Show("⚠️ Database belum dikonfigurasi", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var revenue = await _posDbService.GetTodayRevenueAsync();
                var profit = await _posDbService.GetTodayProfitAsync();
                var transactions = await _posDbService.GetRecentTransactionsAsync(10);

                string msg = $"💰 OMZET HARI INI\n\n";
                msg += $"Revenue: Rp {revenue:N0}\n";
                msg += $"Profit: Rp {profit:N0}\n";
                msg += $"Transaksi: {transactions.Count}\n\n";
                
                if (transactions.Any())
                {
                    msg += "Transaksi Terakhir:\n";
                    foreach (var t in transactions.Take(5))
                    {
                        msg += $"- {t.Date:HH:mm}: Rp {t.Total:N0}\n";
                    }
                }

                MessageBox.Show(msg, "Omzet Hari Ini", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void QuickAction_StokMinus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_posDbService == null)
                {
                    MessageBox.Show("⚠️ Database belum dikonfigurasi", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var products = await _posDbService.GetAllProductsAsync();
                var minusProducts = products.Where(p => p.Stock < 0).ToList();

                if (!minusProducts.Any())
                {
                    MessageBox.Show("✅ Tidak ada produk dengan stok minus", "Stok Minus",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string msg = $"⚠️ STOK MINUS TERDETEKSI\n\n";
                msg += $"Ditemukan {minusProducts.Count} produk dengan stok minus:\n\n";
                
                foreach (var p in minusProducts.Take(10))
                {
                    msg += $"• {p.Name}: {p.Stock} {p.Unit}\n";
                }

                if (minusProducts.Count > 10)
                {
                    msg += $"\n... dan {minusProducts.Count - 10} produk lainnya";
                }

                MessageBox.Show(msg, "Stok Minus", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void QuickAction_Restock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_posDbService == null)
                {
                    MessageBox.Show("⚠️ Database belum dikonfigurasi", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var products = await _posDbService.GetAllProductsAsync();
                var lowStockProducts = products.Where(p => p.Stock > 0 && p.Stock <= 10).ToList();

                if (!lowStockProducts.Any())
                {
                    MessageBox.Show("✅ Semua stok aman! Tidak ada rekomendasi restock", "Restock",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string msg = $"📦 REKOMENDASI RESTOCK\n\n";
                msg += $"Produk dengan stok rendah (≤10):\n\n";
                
                foreach (var p in lowStockProducts.Take(10))
                {
                    msg += $"• {p.Name}: {p.Stock} {p.Unit}\n";
                }

                if (lowStockProducts.Count > 10)
                {
                    msg += $"\n... dan {lowStockProducts.Count - 10} produk lainnya";
                }

                MessageBox.Show(msg, "Rekomendasi Restock", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void QuickAction_SyncDb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("🔄 Sinkronisasi database sekarang?\n\nData akan di-refresh dari pos.db",
                    "Sync Database", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // Show loading
                MessageBox.Show("🔄 Sinkronisasi dimulai...\n\nMohon tunggu beberapa saat.", "Sync Database",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Perform sync
                await RefreshAllDataAsync();

                MessageBox.Show("✅ Sinkronisasi selesai!\n\nSemua data telah di-refresh.", "Sync Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error saat sinkronisasi:\n\n{ex.Message}", "Sync Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshAllDataAsync()
        {
            // Reload current page data
            var currentPage = MainContent.Content;
            if (currentPage is DashboardView dashboard)
            {
                await dashboard.LoadDataAsync();
            }
            else if (currentPage is StockMonitoringView stock)
            {
                await stock.LoadDataAsync();
            }
            else if (currentPage is LogsView logs)
            {
                await logs.LoadDataAsync();
            }
        }

        private void QuickAction_TestConnections_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("🧪 Test semua koneksi?\n\n• Telegram Bot\n• AI (Groq)\n• Database",
                    "Test Connections", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                string results = "🧪 HASIL TEST CONNECTIONS\n\n";

                if (_posDbService != null)
                {
                    results += "💾 Database: ✅ Connected\n";
                }
                else
                {
                    results += "💾 Database: ❌ Not Connected\n";
                }

                var config = _configService.Config;
                if (config?.Groq?.ApiKey != null && config.Groq.ApiKey != "YOUR_GROQ_API_KEY")
                {
                    results += "🧠 Groq AI: ✅ Configured\n";
                }
                else
                {
                    results += "🧠 Groq AI: ❌ Not Configured\n";
                }

                var botSvc = _botController?.GetBotService();
                if (botSvc != null && botSvc.IsRunning)
                {
                    results += "📱 Telegram Bot: ✅ Running\n";
                }
                else
                {
                    results += "📱 Telegram Bot: ❌ Not Running\n";
                }

                MessageBox.Show(results, "Test Connections", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Page Loaders
        private void LoadDashboard()
        {
            var dashboardView = new DashboardView(
                _configService,
                _databaseService,
                _loggingService,
                _posDbService,
                _botController?.GetBotService());

            MainContent.Content = dashboardView;
        }

        private void LoadMonitoring()
        {
            var monitoringView = new StockMonitoringView(
                _configService,
                _databaseService,
                _loggingService,
                _posDbService);

            MainContent.Content = monitoringView;
        }

        private void LoadLogs()
        {
            var logsView = new LogsView(
                _databaseService,
                _loggingService);

            MainContent.Content = logsView;
        }

        private void LoadSettings()
        {
            var settingsView = new SettingsView(
                _configService,
                _posDbService);

            MainContent.Content = settingsView;
        }

        private void LoadAnalytics()
        {
            var analyticsView = new SalesAnalyticsView(
                _configService,
                _databaseService,
                _loggingService,
                _posDbService);

            MainContent.Content = analyticsView;
        }

        private void LoadReports()
        {
            var reportsView = new ReportsView(
                _loggingService,
                _posDbService);

            MainContent.Content = reportsView;
        }

        private void LoadAIChat()
        {
            var groqService = new GroqService(_configService, _loggingService);
            
            var aiChatView = new AIChatView(
                _configService,
                _databaseService,
                _loggingService,
                _posDbService,
                groqService);

            MainContent.Content = aiChatView;
        }
    }
}
