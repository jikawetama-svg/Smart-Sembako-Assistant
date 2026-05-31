using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Views
{
    public enum ProductSearchMode
    {
        Operational,
        Mapping
    }

    public partial class ProductSearchWindow : Window
    {
        private readonly List<Product> _allProducts;
        private readonly ObservableCollection<Product> _filteredProducts = new();
        private readonly ProductSearchMode _mode;

        public Product? SelectedProduct { get; private set; }

        public ProductSearchWindow(
            IEnumerable<Product> products,
            string? initialQuery = null,
            ProductSearchMode mode = ProductSearchMode.Operational)
        {
            InitializeComponent();
            _mode = mode;
            _allProducts = products
                .Where(product => !string.IsNullOrWhiteSpace(product.Id) && !string.IsNullOrWhiteSpace(product.Name))
                .Where(product => _mode == ProductSearchMode.Mapping || product.IsActive)
                .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Title = _mode == ProductSearchMode.Mapping
                ? "Cari Produk Aronium - Mapping/Admin"
                : "Cari Produk Aronium";

            DgProducts.ItemsSource = _filteredProducts;
            TxtSearch.Text = initialQuery ?? string.Empty;
            ApplyFilter();
            TxtSearch.Focus();
            TxtSearch.SelectAll();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void DgProducts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ApplyFilter()
        {
            string query = TxtSearch.Text.Trim();
            IEnumerable<Product> matches = _allProducts;

            if (!string.IsNullOrWhiteSpace(query))
            {
                matches = matches.Where(product =>
                    (product.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (product.Sku?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (product.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            _filteredProducts.Clear();
            foreach (var product in matches.Take(300))
            {
                _filteredProducts.Add(product);
            }

            if (_filteredProducts.Count > 0)
            {
                DgProducts.SelectedIndex = 0;
            }
        }

        private void ConfirmSelection()
        {
            SelectedProduct = DgProducts.SelectedItem as Product;
            if (SelectedProduct == null)
            {
                return;
            }

            DialogResult = true;
        }
    }
}
