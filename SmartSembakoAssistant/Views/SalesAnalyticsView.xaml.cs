using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
        private readonly ExportService _exportService;

        private ObservableCollection<ProductSales> _topProducts = new();
        private ObservableCollection<DailySalesData> _dailySalesData = new();
        private ObservableCollection<ReportRow> _reportRows = new();
        private List<ReportRow> _allReportRows = new();
        private List<SalesLineItem> _allSalesLineItems = new();
        private CancellationTokenSource? _loadCts;
        private string _selectedPeriod = "today";
        private bool _hasLoaded;

        public SalesAnalyticsView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService,
            ExportService exportService)
        {
            InitializeComponent();

            _configService = configService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
            _exportService = exportService;

            DgTopProducts.ItemsSource = _topProducts;
            IcSalesChart.ItemsSource = _dailySalesData;
            DgReports.ItemsSource = _reportRows;

            DpStartDate.SelectedDate = DateTime.Today;
            DpEndDate.SelectedDate = DateTime.Today;

            Loaded += async (_, _) =>
            {
                if (!_hasLoaded)
                {
                    _hasLoaded = true;
                    await LoadSalesAsync();
                }
            };
        }

        private async void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "today";
            DpStartDate.SelectedDate = DateTime.Today;
            DpEndDate.SelectedDate = DateTime.Today;
            UpdatePeriodButtonStyles(BtnToday);
            await LoadSalesAsync();
        }

        private async void BtnThisWeek_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "week";
            var today = DateTime.Today;
            var offset = ((int)today.DayOfWeek + 6) % 7;
            DpStartDate.SelectedDate = today.AddDays(-offset);
            DpEndDate.SelectedDate = today;
            UpdatePeriodButtonStyles(BtnThisWeek);
            await LoadSalesAsync();
        }

        private async void BtnThisMonth_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "month";
            DpStartDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpEndDate.SelectedDate = DateTime.Today;
            UpdatePeriodButtonStyles(BtnThisMonth);
            await LoadSalesAsync();
        }

        private async void BtnApplyDate_Click(object sender, RoutedEventArgs e)
        {
            _selectedPeriod = "custom";
            UpdatePeriodButtonStyles(BtnApplyDate);
            await LoadSalesAsync();
        }

        private void UpdatePeriodButtonStyles(Button activeButton)
        {
            var inactiveStyle = FindResource("PeriodButtonStyle") as Style;
            var activeStyle = FindResource("PeriodButtonActiveStyle") as Style;

            BtnToday.Style = inactiveStyle;
            BtnThisWeek.Style = inactiveStyle;
            BtnThisMonth.Style = inactiveStyle;
            BtnApplyDate.Style = inactiveStyle;
            activeButton.Style = activeStyle;
        }

        private (DateTime StartDate, DateTime EndDate) GetSelectedRange()
        {
            var startDate = DpStartDate.SelectedDate ?? DateTime.Today;
            var endDate = DpEndDate.SelectedDate ?? DateTime.Today;
            return startDate <= endDate ? (startDate, endDate) : (endDate, startDate);
        }

        private async Task LoadSalesAsync()
        {
            if (_posDbService == null)
            {
                TxtSalesStatus.Text = "Database belum dikonfigurasi.";
                ToastHelper.ShowWarning("Database Not Configured", "Configure pos.db in Settings first.");
                return;
            }

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                SetLoading(true);
                var (startDate, endDate) = GetSelectedRange();

                var revenueTask = _posDbService.GetSalesRevenueAsync(startDate, endDate);
                var profitTask = _posDbService.GetSalesProfitAsync(startDate, endDate);
                var transactionTask = _posDbService.GetSalesTransactionCountAsync(startDate, endDate);
                var topProductsTask = _posDbService.GetTopSellingProductsAsync(startDate, endDate, 10);
                var dailySalesTask = _posDbService.GetDailySalesAsync(startDate, endDate);
                var customerTask = _posDbService.GetCustomerPurchasesAsync(startDate, endDate);
                var lineItemsTask = _posDbService.GetSalesLineItemsAsync(startDate, endDate);

                await Task.WhenAll(
                    revenueTask,
                    profitTask,
                    transactionTask,
                    topProductsTask,
                    dailySalesTask,
                    customerTask,
                    lineItemsTask);

                token.ThrowIfCancellationRequested();

                var totalRevenue = await revenueTask;
                var totalProfit = await profitTask;
                var totalTransactions = await transactionTask;
                var topProducts = await topProductsTask;
                var dailySales = await dailySalesTask;
                var customerPurchases = await customerTask;
                var lineItems = await lineItemsTask;

                UpdateSummary(startDate, endDate, totalRevenue, totalProfit, totalTransactions);
                UpdateTopProducts(topProducts);
                UpdateSalesChart(dailySales);
                UpdateCustomerInsights(customerPurchases, startDate, endDate);
                UpdateReportRows(lineItems);

                TxtSalesStatus.Text = $"Periode {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}. Menampilkan {_reportRows.Count:N0} baris detail.";
            }
            catch (OperationCanceledException)
            {
                // Filter terbaru sedang diproses.
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error loading sales module: {ex.Message}",
                    "Sales",
                    ex.ToString());

                ToastHelper.ShowError("Load Failed", ex.Message);
                TxtSalesStatus.Text = $"Gagal memuat data: {ex.Message}";
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void SetLoading(bool isLoading)
        {
            BtnToday.IsEnabled = !isLoading;
            BtnThisWeek.IsEnabled = !isLoading;
            BtnThisMonth.IsEnabled = !isLoading;
            BtnApplyDate.IsEnabled = !isLoading;
            BtnExportCSV.IsEnabled = !isLoading;
            BtnExportExcel.IsEnabled = !isLoading;
            BtnExportPDF.IsEnabled = !isLoading;
            TxtLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSummary(DateTime startDate, DateTime endDate, decimal totalRevenue, decimal totalProfit, int totalTransactions)
        {
            var avgTransaction = totalTransactions > 0 ? totalRevenue / totalTransactions : 0;
            var profitMargin = totalRevenue > 0 ? totalProfit / totalRevenue * 100 : 0;
            var periodLabel = _selectedPeriod switch
            {
                "today" => "Hari ini",
                "week" => "Minggu ini",
                "month" => "Bulan ini",
                _ => $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}"
            };

            TxtTotalRevenue.Text = $"Rp {totalRevenue:N0}";
            TxtTotalProfit.Text = $"Rp {totalProfit:N0}";
            TxtProfitMargin.Text = $"Margin: {profitMargin:F1}%";
            TxtTotalTransactions.Text = totalTransactions.ToString("N0");
            TxtAvgTransaction.Text = $"Rp {avgTransaction:N0}";
            TxtRevenueLabel.Text = periodLabel;
            TxtTransactionLabel.Text = $"{totalTransactions:N0} transaksi";
            TxtAvgLabel.Text = "Per transaksi";
        }

        private void UpdateTopProducts(List<ProductSalesData> topProducts)
        {
            var ranked = topProducts.Select((product, index) => new ProductSales
            {
                Rank = index + 1,
                ProductName = product.ProductName,
                QuantitySold = (int)product.QuantitySold,
                Revenue = product.Revenue,
                Profit = product.Profit
            });

            _topProducts = new ObservableCollection<ProductSales>(ranked);
            DgTopProducts.ItemsSource = _topProducts;
        }

        private void UpdateSalesChart(List<DailySalesData> dailySales)
        {
            _dailySalesData = new ObservableCollection<DailySalesData>();

            if (dailySales.Count == 0)
            {
                TxtChartNoData.Visibility = Visibility.Visible;
                IcSalesChart.ItemsSource = _dailySalesData;
                return;
            }

            TxtChartNoData.Visibility = Visibility.Collapsed;
            var limitedSales = dailySales.Take(14).ToList();
            var maxRevenue = limitedSales.Max(s => s.Revenue);

            foreach (var sale in limitedSales)
            {
                sale.BarWidth = maxRevenue > 0
                    ? Math.Max(4, (double)(sale.Revenue / maxRevenue) * 520)
                    : 4;
                _dailySalesData.Add(sale);
            }

            IcSalesChart.ItemsSource = _dailySalesData;
        }

        private void UpdateCustomerInsights(List<CustomerPurchaseInfo> customerPurchases, DateTime startDate, DateTime endDate)
        {
            TxtUniqueCustomers.Text = customerPurchases.Count.ToString("N0");

            var bestCustomer = customerPurchases
                .OrderByDescending(c => c.PurchaseCount)
                .ThenByDescending(c => c.TotalSpent)
                .FirstOrDefault();

            TxtBestCustomer.Text = bestCustomer?.Name ?? "-";
            TxtBestCustomerSpent.Text = bestCustomer == null
                ? "Tidak ada data"
                : $"Rp {bestCustomer.TotalSpent:N0} ({bestCustomer.PurchaseCount:N0} transaksi)";

            var daysInRange = Math.Max(1, (int)(endDate - startDate).TotalDays + 1);
            var totalTxCount = customerPurchases.Sum(c => c.PurchaseCount);
            var txPerDay = totalTxCount > 0 ? (decimal)totalTxCount / daysInRange : 0;
            TxtTxPerDay.Text = txPerDay.ToString("F1");
        }

        private void UpdateReportRows(List<SalesLineItem> lineItems)
        {
            _allSalesLineItems = lineItems
                .OrderByDescending(item => item.Date)
                .ToList();

            _allReportRows = _allSalesLineItems
                .Select(li => new ReportRow
                {
                    Date = li.Date,
                    Invoice = li.Invoice ?? "-",
                    ProductName = li.ProductName ?? "Unknown",
                    Quantity = (int)li.Quantity,
                    Price = li.Price,
                    Total = li.Total,
                    Profit = li.Profit
                })
                .ToList();

            _reportRows = new ObservableCollection<ReportRow>(_allReportRows);
            DgReports.ItemsSource = _reportRows;
            TxtReportInfo.Text = $"Menampilkan {_reportRows.Count:N0} baris data";
        }

        public async Task LoadDataAsync()
        {
            await LoadSalesAsync();
        }

        private async void BtnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            await ExportWithDialogAsync(
                ExportFormat.Csv,
                "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                "csv");
        }

        private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            await ExportWithDialogAsync(
                ExportFormat.Excel,
                "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                "xlsx");
        }

        private async void BtnExportPDF_Click(object sender, RoutedEventArgs e)
        {
            await ExportWithDialogAsync(
                ExportFormat.Pdf,
                "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                "pdf");
        }

        private async Task ExportWithDialogAsync(ExportFormat format, string filter, string defaultExt)
        {
            try
            {
                if (_allSalesLineItems.Count == 0)
                {
                    ToastHelper.ShowInfo("No Data", "Tidak ada data untuk di-export.", Window.GetWindow(this));
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = filter,
                    DefaultExt = defaultExt,
                    FileName = $"penjualan_{DateTime.Now:yyyyMMdd_HHmmss}.{defaultExt}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var result = await _exportService.ExportSalesAsync(
                        _allSalesLineItems,
                        saveDialog.FileName,
                        format,
                        GetExportPeriodLabel());

                    if (result.Success)
                    {
                        ToastHelper.ShowSuccess("Export Success", $"{result.Message} {result.FilePath}", Window.GetWindow(this));
                    }
                    else
                    {
                        ToastHelper.ShowError("Export Failed", result.Message, Window.GetWindow(this));
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Error exporting sales report: {ex.Message}", "Sales", ex.ToString());
                ToastHelper.ShowError("Export Failed", $"Gagal export: {ex.Message}", Window.GetWindow(this));
            }
        }

        private string GetExportPeriodLabel()
        {
            var (startDate, endDate) = GetSelectedRange();
            return _selectedPeriod switch
            {
                "today" => "Hari Ini",
                "week" => "Minggu Ini",
                "month" => "Bulan Ini",
                _ => $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}"
            };
        }
    }

    public class ProductSales
    {
        public int Rank { get; set; }
        public string? ProductName { get; set; }
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
    }
}
