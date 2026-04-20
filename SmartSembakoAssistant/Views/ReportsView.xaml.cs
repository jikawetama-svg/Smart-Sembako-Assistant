using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartSembakoAssistant.Controls;
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
        public decimal Total { get; set; }
        public decimal Profit { get; set; }
    }

    public partial class ReportsView : UserControl
    {
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;

        private ObservableCollection<ReportRow> _reportRows = new();
        private List<ReportRow> _allReportRows = new();

        public ReportsView(
            LoggingService loggingService,
            PosDbService? posDbService)
        {
            InitializeComponent();

            _loggingService = loggingService;
            _posDbService = posDbService;

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

                // Convert line items to report rows
                _allReportRows = lineItems.Select(li => new ReportRow
                {
                    Date = li.Date,
                    Invoice = li.Invoice ?? "-",
                    ProductName = li.ProductName ?? "Unknown",
                    Quantity = (int)li.Quantity,
                    Price = li.Price,
                    Total = li.Total,
                    Profit = li.Profit
                }).ToList();

                // Sort by date descending
                _allReportRows.Sort((a, b) => b.Date.CompareTo(a.Date));

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

        private void BtnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_reportRows.Count == 0)
                {
                    ToastHelper.ShowInfo(
                        "Info",
                        "Tidak ada data untuk di-export.");
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"reports_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportToCSV(saveDialog.FileName);

                    ToastHelper.ShowSuccess(
                        "Export CSV",
                        $"Data exported successfully to {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync(
                    $"Error exporting CSV: {ex.Message}",
                    "Reports",
                    ex.ToString());

                ToastHelper.ShowError(
                    "Export Failed",
                    $"Unable to export CSV: {ex.Message}");
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_reportRows.Count == 0)
                {
                    ToastHelper.ShowInfo(
                        "No Data",
                        "Tidak ada data untuk di-export.",
                        Window.GetWindow(this));
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"reports_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Export as CSV (compatible with Excel)
                    ExportToCSV(saveDialog.FileName);

                    ToastHelper.ShowSuccess(
                        "Export Success",
                        $"Data exported to CSV: {saveDialog.FileName}",
                        Window.GetWindow(this));
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync(
                    $"Error exporting CSV: {ex.Message}",
                    "Reports",
                    ex.ToString());

                ToastHelper.ShowError(
                    "Export Failed",
                    $"Gagal export CSV: {ex.Message}",
                    Window.GetWindow(this));
            }
        }

        private void BtnExportPDF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_reportRows.Count == 0)
                {
                    ToastHelper.ShowInfo(
                        "No Data",
                        "Tidak ada data untuk di-export.",
                        Window.GetWindow(this));
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportToTextFile(saveDialog.FileName);

                    ToastHelper.ShowSuccess(
                        "Export Success",
                        $"Report exported to text file: {saveDialog.FileName}",
                        Window.GetWindow(this));
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

        private void ExportToCSV(string filePath)
        {
            // UTF-8 with BOM for Excel compatibility
            var encoding = new UTF8Encoding(true);
            var lines = new List<string>
            {
                "Tanggal,Invoice,Produk,Qty,Harga,Total,Profit"
            };

            foreach (var row in _allReportRows)
            {
                lines.Add($"{row.Date:dd/MM/yyyy}," +
                          $"\"{EscapeCsvField(row.Invoice)}\"," +
                          $"\"{EscapeCsvField(row.ProductName)}\"," +
                          $"{row.Quantity}," +
                          $"{row.Price}," +
                          $"{row.Total}," +
                          $"{row.Profit}");
            }

            File.WriteAllLines(filePath, lines, encoding);
        }

        private void ExportToTextFile(string filePath)
        {
            var lines = new List<string>
            {
                "==================================================",
                "LAPORAN PENJUALAN - SMART SEMBAKO ASSISTANT",
                "==================================================",
                $"",
                $"Tanggal Export: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                $"Total Transaksi: {_reportRows.Count}",
                $"",
                "--------------------------------------------------",
                "DETAIL TRANSAKSI",
                "--------------------------------------------------",
                ""
            };

            foreach (var row in _reportRows)
            {
                lines.Add($"Tanggal   : {row.Date:dd/MM/yyyy}");
                lines.Add($"Invoice   : {row.Invoice}");
                lines.Add($"Produk    : {row.ProductName}");
                lines.Add($"Qty       : {row.Quantity}");
                lines.Add($"Harga     : Rp {row.Price:N0}");
                lines.Add($"Total     : Rp {row.Total:N0}");
                lines.Add($"Profit    : Rp {row.Profit:N0}");
                lines.Add($"");
                lines.Add($"--------------------------------------------------");
                lines.Add($"");
            }

            lines.Add("");
            lines.Add("==================================================");
            lines.Add("RINGKASAN");
            lines.Add("==================================================");
            lines.Add($"Total Revenue : Rp {_reportRows.Sum(r => r.Total):N0}");
            lines.Add($"Total Profit  : Rp {_reportRows.Sum(r => r.Profit):N0}");
            lines.Add($"Items Sold    : {_reportRows.Sum(r => r.Quantity)}");
            lines.Add($"");
            lines.Add("==================================================");

            File.WriteAllLines(filePath, lines, new UTF8Encoding(true));
        }

        private void ExportToExcel(string filePath)
        {
            // Export as CSV with .xlsx extension as a temporary workaround
            // (since we don't have ClosedXML/EPPlus)
            var encoding = new UTF8Encoding(true);
            var lines = new List<string>
            {
                "Tanggal,Invoice,Produk,Qty,Harga,Total,Profit"
            };

            foreach (var row in _allReportRows)
            {
                lines.Add($"{row.Date:dd/MM/yyyy}," +
                          $"\"{EscapeCsvField(row.Invoice)}\"," +
                          $"\"{EscapeCsvField(row.ProductName)}\"," +
                          $"{row.Quantity}," +
                          $"{row.Price}," +
                          $"{row.Total}," +
                          $"{row.Profit}");
            }

            File.WriteAllLines(filePath, lines, encoding);
        }

        private string EscapeCsvField(string? field)
        {
            if (field == null) return "";
            return field.Replace("\"", "\"\"");
        }
    }
}
