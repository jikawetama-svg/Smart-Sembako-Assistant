using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class StockMonitoringView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        
        private ObservableCollection<Product> _products = new();
        private List<Product> _allProducts = new();

        public StockMonitoringView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService)
        {
            InitializeComponent();

            _configService = configService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;

            LvProducts.ItemsSource = _products;
            LoadProducts();
        }

        private async void LoadProducts()
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowWarning("Database Not Configured", "Database pos.db belum dikonfigurasi.\n\nSilakan konfigurasi di Settings terlebih dahulu.", Window.GetWindow(this));
                return;
            }

            try
            {
                _allProducts = await _posDbService.GetAllProductsAsync();
                _products.Clear();
                foreach (var product in _allProducts)
                {
                    _products.Add(product);
                }

                // Update quick stats
                UpdateQuickStats();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error loading products: {ex.Message}",
                    "Monitoring",
                    ex.ToString());

                ToastHelper.ShowError("Error", $"Error loading products: {ex.Message}", Window.GetWindow(this));
            }
        }

        private void UpdateQuickStats()
        {
            int safeStock = _allProducts.Count(p => p.Stock > 10);
            int lowStock = _allProducts.Count(p => p.Stock > 0 && p.Stock <= 10);
            int outStock = _allProducts.Count(p => p.Stock == 0);
            int negativeStock = _allProducts.Count(p => p.Stock < 0);

            TxtSafeStock.Text = safeStock.ToString();
            TxtLowStock.Text = lowStock.ToString();
            TxtOutStock.Text = outStock.ToString();
            TxtNegativeStock.Text = negativeStock.ToString();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProducts();
        }

        private void BtnAll_Click(object sender, RoutedEventArgs e)
        {
            FilterProducts();
        }

        private void BtnLowStock_Click(object sender, RoutedEventArgs e)
        {
            FilterProducts(lowStockOnly: true);
        }

        private void BtnExpiring_Click(object sender, RoutedEventArgs e)
        {
            FilterProducts(expiringOnly: true);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnRefresh.IsEnabled = false;
            BtnRefresh.Content = "⏳ Refreshing...";

            await Task.Delay(500); // Small delay for UX
            LoadProducts();

            BtnRefresh.IsEnabled = true;
            BtnRefresh.Content = "🔄 Refresh";
        }

        private void FilterProducts(bool lowStockOnly = false, bool expiringOnly = false)
        {
            string searchText = TxtSearch.Text?.ToLower() ?? "";

            var filtered = _allProducts.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(p =>
                    p.Name != null && p.Name.ToLower().Contains(searchText));
            }

            // Apply low stock filter - handle null Stock properly
            if (lowStockOnly)
            {
                filtered = filtered.Where(p => p.Stock.HasValue && p.Stock <= 20);
            }

            // Apply expiring filter
            if (expiringOnly)
            {
                filtered = filtered.Where(p =>
                    p.ExpiryDate.HasValue && 
                    p.ExpiryDate.Value <= DateTime.Now.AddDays(30));
            }

            _products.Clear();
            foreach (var product in filtered)
            {
                _products.Add(product);
            }
        }

        /// <summary>
        /// Public method to reload data (called from MainWindow sync)
        /// </summary>
        public async Task LoadDataAsync()
        {
            LoadProducts();
        }
    }
}
