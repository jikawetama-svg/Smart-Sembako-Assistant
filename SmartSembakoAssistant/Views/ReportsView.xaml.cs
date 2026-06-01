using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    /// <summary>
    /// Represents a single row in the reports data grid.
    /// </summary>
    public class ReportRow
    {
        public DateTime Date { get; set; }
        public string? Invoice { get; set; }
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Cost { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal Total { get; set; }
        public decimal Profit { get; set; }
    }

    public partial class ReportsView : UserControl
    {
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private readonly ExportService _exportService;

        private ObservableCollection<ReportRow> _reportRows = new();
        private List<ReportRow> _allReportRows = new();
        private List<SalesLineItem> _allSalesLineItems = new();

        public ReportsView(
            LoggingService loggingService,
            PosDbService? posDbService,
            ExportService exportService)
        {
            InitializeComponent();

            _loggingService = loggingService;
            _posDbService = posDbService;
            _exportService = exportService;

            DgReports.ItemsSource = _reportRows;

            // Set default date range to today
            DpStartDate.SelectedDate = DateTime.Today;
            DpEndDate.SelectedDate = DateTime.Today;

            // Load data setelah UI ready
            Loaded += async (s, e) => await LoadReportsData();
        }

        /// <summary>
        /// Public method to reload data (called from MainWindow sync)
        /// </summary>
        public async Task LoadDataAsync()
        {
            await LoadReportsData();
        }

        private async Task LoadReportsData()
        {
            if (_posDbService == null)
            {
                TxtReportInfo.Text = "Database belum dikonfigurasi.";
                return;
            }

            try
            {
                var startDate = DpStartDate.SelectedDate ?? DateTime.Today;
                var endDate = DpEndDate.SelectedDate ?? DateTime.Today;

                if (startDate > endDate)
                {
                    TxtReportInfo.Text = "Tanggal awal tidak boleh lebih besar dari tanggal akhir.";
                    return;
                }

                // Fetch summary data using PosDbService methods
                var totalSales = await _posDbService.GetSalesRevenueAsync(startDate, endDate);
                var totalProfit = await _posDbService.GetSalesProfitAsync(startDate, endDate);
                var transactionCount = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);

                // Fetch detailed line items for DataGrid
                var lineItems = await _posDbService.GetSalesLineItemsAsync(startDate, endDate);

                // Calculate total items sold from line items
                int totalItemsSold = (int)lineItems.Sum(li => li.Quantity);

                _allSalesLineItems = lineItems
                    .OrderByDescending(item => item.Date)
                    .ToList();

                // Convert line items to report rows
                _allReportRows = _allSalesLineItems.Select(li => new ReportRow
                {
                    Date = li.Date,
                    Invoice = li.Invoice ?? "-",
                    ProductName = li.ProductName ?? "Unknown",
                    Quantity = (int)li.Quantity,
                    Price = li.Price,
                    Cost = li.Cost,
                    DiscountAmount = li.DiscountAmount + li.DocumentDiscountAmount,
                    TaxAmount = li.TaxAmount,
                    Total = li.Total,
                    Profit = li.Profit
                }).ToList();

                _reportRows.Clear();
                foreach (var row in _allReportRows)
                {
                    _reportRows.Add(row);
                }

                // Fetch top product for summary card
                var topProducts = await _posDbService.GetTopSellingProductsAsync(startDate, endDate, limit: 1);

                // Update summary cards
                TxtTotalSales.Text = $"Rp {totalSales:N0}";
                TxtTotalProfit.Text = $"Rp {totalProfit:N0}";
                TxtItemsSold.Text = totalItemsSold.ToString();
                TxtTransactionCount.Text = $"{transactionCount} transaksi";
                TxtSalesPeriod.Text = $"{startDate:dd/MM} - {endDate:dd/MM}";

                decimal margin = totalSales > 0 ? (totalProfit / totalSales) * 100 : 0;
                TxtProfitMargin.Text = $"Margin: {margin:F1}%";

                // Top product
                if (topProducts.Count > 0)
                {
                    var topProduct = topProducts[0];
                    TxtTopProduct.Text = topProduct.ProductName ?? "-";
                    TxtTopProductQty.Text = $"{(int)topProduct.QuantitySold} terjual";
                }
                else
                {
                    TxtTopProduct.Text = "-";
                    TxtTopProductQty.Text = "0 terjual";
                }

                TxtReportInfo.Text = $"Menampilkan: {_reportRows.Count} baris data";
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error loading reports: {ex.Message}",
                    "Reports",
                    ex.ToString());

                ToastHelper.ShowError(
                    "Data Error",
                    $"Gagal memuat laporan: {ex.Message}");
            }
        }

        private async void BtnApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportsData();
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
                    ToastHelper.ShowInfo(
                        "No Data",
                        "Tidak ada data untuk di-export.",
                        Window.GetWindow(this));
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = filter,
                    DefaultExt = defaultExt,
                    FileName = $"reports_{DateTime.Now:yyyyMMdd_HHmmss}.{defaultExt}"
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
                        ToastHelper.ShowSuccess(
                            "Export Success",
                            $"{result.Message} {result.FilePath}",
                            Window.GetWindow(this));
                    }
                    else
                    {
                        ToastHelper.ShowError(
                            "Export Failed",
                            result.Message,
                            Window.GetWindow(this));
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync(
                    $"Error exporting report: {ex.Message}",
                    "Reports",
                    ex.ToString());

                ToastHelper.ShowError(
                    "Export Failed",
                    $"Gagal export report: {ex.Message}",
                    Window.GetWindow(this));
            }
        }

        private string GetExportPeriodLabel()
        {
            var startDate = DpStartDate.SelectedDate ?? DateTime.Today;
            var endDate = DpEndDate.SelectedDate ?? DateTime.Today;
            return $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}";
        }
    }
}
