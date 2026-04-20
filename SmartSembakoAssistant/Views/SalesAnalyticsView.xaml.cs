using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class SalesAnalyticsView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;

        private ObservableCollection<ProductSales> _topProducts = new();
        private ObservableCollection<DailySalesData> _dailySalesData = new();
        private string _selectedPeriod = "today";

        public SalesAnalyticsView(
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

            DgTopProducts.ItemsSource = _topProducts;
            IcSalesChart.ItemsSource = _dailySalesData;

            // Set default date values
            DpStartDate.SelectedDate = DateTime.Today;
            DpEndDate.SelectedDate = DateTime.Today;

            LoadAnalytics();
        }

        // Period selector handlers
        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "today";
            UpdatePeriodButtonStyles(BtnToday);
            PanelCustomDate.Visibility = Visibility.Collapsed;
            LoadAnalytics();
        }

        private void BtnThisWeek_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "week";
            UpdatePeriodButtonStyles(BtnThisWeek);
            PanelCustomDate.Visibility = Visibility.Collapsed;
            LoadAnalytics();
        }

        private void BtnThisMonth_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "month";
            UpdatePeriodButtonStyles(BtnThisMonth);
            PanelCustomDate.Visibility = Visibility.Collapsed;
            LoadAnalytics();
        }

        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "custom";
            UpdatePeriodButtonStyles(BtnCustom);
            PanelCustomDate.Visibility = Visibility.Visible;
        }

        private void BtnApplyDate_Click(object sender, RoutedEventArgs e)
        {
            LoadAnalytics();
        }

        private void UpdatePeriodButtonStyles(Button activeButton)
        {
            // Reset all to inactive style
            var inactiveStyle = FindResource("PeriodButtonStyle") as Style;
            var activeStyle = FindResource("PeriodButtonActiveStyle") as Style;

            BtnToday.Style = inactiveStyle;
            BtnThisWeek.Style = inactiveStyle;
            BtnThisMonth.Style = inactiveStyle;
            BtnCustom.Style = inactiveStyle;

            // Set active button to active style
            activeButton.Style = activeStyle;
        }

        private async void LoadAnalytics()
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowWarning("Database Not Configured", "Configure pos.db in Settings first.");
                return;
            }

            try
            {
                DateTime startDate, endDate;

                switch (_selectedPeriod)
                {
                    case "today":
                        startDate = DateTime.Today;
                        endDate = DateTime.Today;
                        break;

                    case "week":
                        startDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
                        endDate = DateTime.Today;
                        break;

                    case "month":
                        startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                        endDate = DateTime.Today;
                        break;

                    case "custom":
                    default:
                        startDate = DpStartDate.SelectedDate ?? DateTime.Today.AddDays(-7);
                        endDate = DpEndDate.SelectedDate ?? DateTime.Today;
                        break;
                }

                // Get real data from pos.db - only sales transactions (DocumentTypeId = 2, TypeCode 200)
                decimal totalRevenue = await _posDbService.GetSalesRevenueAsync(startDate, endDate);
                decimal totalProfit = await _posDbService.GetSalesProfitAsync(startDate, endDate);
                int totalTransactions = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);

                decimal avgTransaction = totalTransactions > 0 ? totalRevenue / totalTransactions : 0;
                decimal profitMargin = totalRevenue > 0 ? (totalProfit / totalRevenue) * 100 : 0;

                // Update summary cards
                TxtTotalRevenue.Text = $"Rp {totalRevenue:N0}";
                TxtTotalProfit.Text = $"Rp {totalProfit:N0}";
                TxtProfitMargin.Text = $"Margin: {profitMargin:F1}%";
                TxtTotalTransactions.Text = totalTransactions.ToString();
                TxtAvgTransaction.Text = $"Rp {avgTransaction:N0}";

                // Update period labels
                string periodLabel = _selectedPeriod switch
                {
                    "today" => "Hari ini",
                    "week" => "Minggu ini",
                    "month" => "Bulan ini",
                    "custom" => $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}",
                    _ => "Periode"
                };
                TxtRevenueLabel.Text = periodLabel;
                TxtTransactionLabel.Text = $"{totalTransactions} transaksi";
                TxtAvgLabel.Text = "Per transaksi";

                // Get top 10 products by quantity sold (real data from DocumentItem)
                var topProductsData = await _posDbService.GetTopSellingProductsAsync(startDate, endDate, 10);
                _topProducts.Clear();
                int rank = 1;
                foreach (var product in topProductsData)
                {
                    _topProducts.Add(new ProductSales
                    {
                        Rank = rank++,
                        ProductName = product.ProductName,
                        QuantitySold = (int)product.QuantitySold,
                        Revenue = product.Revenue,
                        Profit = product.Profit
                    });
                }

                // Update sales chart
                await UpdateSalesChartAsync(startDate, endDate);

                // Customer insights from real Customer table
                await UpdateCustomerInsightsAsync(startDate, endDate);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error loading sales analytics: {ex.Message}",
                    "SalesAnalytics",
                    ex.ToString());

                ToastHelper.ShowError("Load Failed", ex.Message);
            }
        }

        private async Task UpdateSalesChartAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var dailySales = await _posDbService.GetDailySalesAsync(startDate, endDate);

                _dailySalesData.Clear();

                if (dailySales == null || dailySales.Count == 0)
                {
                    TxtChartNoData.Visibility = Visibility.Visible;
                    return;
                }

                TxtChartNoData.Visibility = Visibility.Collapsed;

                // Limit to max 10 bars for clean display
                var limitedSales = dailySales.Take(10).ToList();

                // Find max revenue for scaling
                decimal maxRevenue = limitedSales.Max(s => s.Revenue);

                // Calculate bar widths (percentage of available space, will be scaled in XAML)
                // Estimate available bar area width (total width minus date label ~70px and revenue label ~100px)
                // We use a base width of ~300px as typical available space
                double estimatedBarAreaWidth = 300;

                foreach (var sale in limitedSales)
                {
                    double barWidth = maxRevenue > 0
                        ? (double)sale.Revenue / (double)maxRevenue * estimatedBarAreaWidth
                        : 4; // minimum width

                    sale.BarWidth = Math.Max(4, barWidth); // minimum 4px

                    _dailySalesData.Add(sale);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync(
                    $"Error updating sales chart: {ex.Message}", "SalesAnalytics");

                _dailySalesData.Clear();
                TxtChartNoData.Visibility = Visibility.Visible;
            }
        }

        private async Task UpdateCustomerInsightsAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                // Get customer purchase data from real Customer table
                var customerPurchases = await _posDbService.GetCustomerPurchasesAsync(startDate, endDate);

                // Unique customers who made purchases
                int uniqueCustomers = customerPurchases.Count;
                TxtUniqueCustomers.Text = uniqueCustomers.ToString();

                // Best customer (most transactions, then highest total as tiebreaker)
                var bestCustomer = customerPurchases
                    .OrderByDescending(c => c.PurchaseCount)
                    .ThenByDescending(c => c.TotalSpent)
                    .FirstOrDefault();

                if (bestCustomer != null)
                {
                    TxtBestCustomer.Text = bestCustomer.Name ?? "-";
                    TxtBestCustomerSpent.Text = $"Rp {bestCustomer.TotalSpent:N0} ({bestCustomer.PurchaseCount} transaksi)";
                }
                else
                {
                    TxtBestCustomer.Text = "-";
                    TxtBestCustomerSpent.Text = "Tidak ada data";
                }

                // Calculate transactions per day
                int daysInRange = Math.Max(1, (int)(endDate - startDate).TotalDays + 1);
                int totalTxCount = customerPurchases.Sum(c => c.PurchaseCount);
                decimal txPerDay = totalTxCount > 0
                    ? (decimal)totalTxCount / daysInRange
                    : 0;

                TxtTxPerDay.Text = txPerDay.ToString("F1");
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync(
                    $"Error updating customer insights: {ex.Message}", "SalesAnalytics");

                TxtUniqueCustomers.Text = "-";
                TxtBestCustomer.Text = "Error";
                TxtTxPerDay.Text = "-";
            }
        }

        /// <summary>
        /// Public method to reload data (called from MainWindow sync)
        /// </summary>
        public async Task LoadDataAsync()
        {
            LoadAnalytics();
        }
    }

    /// <summary>
    /// Data model for product sales analytics
    /// </summary>
    public class ProductSales
    {
        public int Rank { get; set; }
        public string? ProductName { get; set; }
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
    }

    /// <summary>
    /// Data model for analytics summary
    /// </summary>
    public class AnalyticsSummary
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalProfit { get; set; }
        public int TotalTransactions { get; set; }
        public decimal AverageTransaction { get; set; }
        public decimal ProfitMargin { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
