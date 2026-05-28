using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
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

        private readonly ObservableCollection<StockDisplayItem> _products = new();
        private ICollectionView? _productsView;
        private DispatcherTimer? _searchDebounceTimer;
        private string _activeStockFilter = "Semua";
        private bool _hasLoaded;

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

            _productsView = CollectionViewSource.GetDefaultView(_products);
            _productsView.Filter = ProductFilter;
            DgProducts.ItemsSource = _productsView;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer?.Stop();
                RefreshProductView();
            };

            Loaded += async (_, _) =>
            {
                if (!_hasLoaded)
                {
                    _hasLoaded = true;
                    await LoadProductsAsync();
                }
            };
        }

        private async Task LoadProductsAsync()
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowWarning(
                    "Database Not Configured",
                    "Database pos.db belum dikonfigurasi. Silakan konfigurasi di Settings terlebih dahulu.",
                    Window.GetWindow(this));
                TxtProductCount.Text = "Database belum dikonfigurasi.";
                return;
            }

            try
            {
                SetLoading(true);
                var products = await _posDbService.GetAllProductsAsync();
                var displayItems = await BuildStockDisplayItemsAsync(products);

                _products.Clear();
                foreach (var product in displayItems)
                {
                    _products.Add(product);
                }

                UpdateQuickStats();
                RefreshProductView();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error loading products: {ex.Message}",
                    "Monitoring",
                    ex.ToString());

                ToastHelper.ShowError("Error", $"Error loading products: {ex.Message}", Window.GetWindow(this));
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void SetLoading(bool isLoading)
        {
            TxtLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            BtnRefresh.IsEnabled = !isLoading;
            BtnRefresh.Content = isLoading ? "Memuat..." : "Refresh";
        }

        private void UpdateQuickStats()
        {
            TxtTotalStock.Text = _products.Count.ToString();
            TxtSafeStock.Text = _products.Count(p => p.Stock > 10).ToString();
            TxtLowStock.Text = _products.Count(p => p.Stock > 0 && p.Stock <= 10).ToString();
            TxtOutStock.Text = _products.Count(p => p.Stock == 0).ToString();
            TxtNegativeStock.Text = _products.Count(p => !p.Stock.HasValue || p.Stock < 0).ToString();
        }

        private bool ProductFilter(object item)
        {
            if (item is not Product product)
            {
                return false;
            }

            var searchText = TxtSearch?.Text?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var matchesName = product.Name?.ToLowerInvariant().Contains(searchText) == true;
                var matchesSku = product.Sku?.ToLowerInvariant().Contains(searchText) == true;
                var matchesFamily = item is StockDisplayItem displayItem &&
                                    displayItem.ConversionRelationText?.ToLowerInvariant().Contains(searchText) == true;
                if (!matchesName && !matchesSku && !matchesFamily)
                {
                    return false;
                }
            }

            return _activeStockFilter switch
            {
                "Minus" => !product.Stock.HasValue || product.Stock < 0,
                "Habis" => product.Stock == 0,
                "Rendah" => product.Stock > 0 && product.Stock <= 10,
                "Aman" => product.Stock > 10,
                _ => true
            };
        }

        private void RefreshProductView()
        {
            _productsView?.Refresh();
            UpdateFooterCount();
        }

        private void UpdateFooterCount()
        {
            var filteredCount = _productsView?.Cast<object>().Count() ?? 0;
            TxtProductCount.Text = $"Menampilkan {filteredCount:N0} dari {_products.Count:N0} produk (Filter: {_activeStockFilter})";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }

        private void StockFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filter)
            {
                _activeStockFilter = filter;
                UpdateFilterButtonStyles(button);
                RefreshProductView();
            }
        }

        private void UpdateFilterButtonStyles(Button activeButton)
        {
            var inactiveStyle = FindResource("FilterButtonStyle") as Style;
            var activeStyle = FindResource("FilterButtonActiveStyle") as Style;

            BtnAll.Style = inactiveStyle;
            BtnMinus.Style = inactiveStyle;
            BtnOut.Style = inactiveStyle;
            BtnLowStock.Style = inactiveStyle;
            BtnSafe.Style = inactiveStyle;

            activeButton.Style = activeStyle;
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadProductsAsync();
        }

        public async Task LoadDataAsync()
        {
            await LoadProductsAsync();
        }

        private async Task<List<StockDisplayItem>> BuildStockDisplayItemsAsync(List<Product> products)
        {
            var displayItems = products.Select(StockDisplayItem.FromProduct).ToList();
            var productById = displayItems
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .GroupBy(product => product.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            foreach (var mapping in mappings.Where(mapping => mapping.ConversionRate > 0))
            {
                if (!productById.TryGetValue(mapping.ParentProductId, out var parent) ||
                    !productById.TryGetValue(mapping.ChildProductId, out var child))
                {
                    continue;
                }

                decimal parentStock = parent.Stock ?? 0;
                decimal childStock = child.Stock ?? 0;
                decimal totalChild = parentStock * mapping.ConversionRate + childStock;
                decimal totalParent = totalChild / mapping.ConversionRate;
                string parentUnit = string.IsNullOrWhiteSpace(parent.Unit) ? "unit besar" : parent.Unit!;
                string childUnit = string.IsNullOrWhiteSpace(child.Unit) ? "unit kecil" : child.Unit!;
                string relation = $"1 {parentUnit} = {mapping.ConversionRate:0.##} {childUnit}";

                parent.EffectiveStock = totalParent;
                parent.EffectiveStockText = $"{totalParent:0.##} {parentUnit}";
                parent.ConversionRelationText = $"{relation}; setara {totalChild:0.##} {childUnit}";

                child.EffectiveStock = totalChild;
                child.EffectiveStockText = $"{totalChild:0.##} {childUnit}";
                child.ConversionRelationText = $"{relation}; gabungan {parentStock:0.##} {parentUnit} + {childStock:0.##} {childUnit}";
            }

            foreach (var item in displayItems.Where(item => string.IsNullOrWhiteSpace(item.EffectiveStockText)))
            {
                item.EffectiveStock = item.Stock ?? 0;
                item.EffectiveStockText = "-";
                item.ConversionRelationText = "Produk belum punya mapping dual stok.";
            }

            return displayItems;
        }

        public class StockDisplayItem : Product
        {
            public decimal? EffectiveStock { get; set; }
            public string? EffectiveStockText { get; set; }
            public string? ConversionRelationText { get; set; }

            public static StockDisplayItem FromProduct(Product product)
            {
                return new StockDisplayItem
                {
                    Id = product.Id,
                    Name = product.Name,
                    Sku = product.Sku,
                    Category = product.Category,
                    Stock = product.Stock,
                    PurchasePrice = product.PurchasePrice,
                    SellingPrice = product.SellingPrice,
                    Unit = product.Unit,
                    ExpiryDate = product.ExpiryDate,
                    BatchNumber = product.BatchNumber,
                    IsActive = product.IsActive,
                    SoldThisMonth = product.SoldThisMonth
                };
            }
        }
    }
}
