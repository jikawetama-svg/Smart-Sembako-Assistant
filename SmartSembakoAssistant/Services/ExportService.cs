using System.IO;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class ExportService
    {
        private readonly LoggingService _loggingService;

        public ExportService(LoggingService loggingService)
        {
            _loggingService = loggingService;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<ExportResult> ExportSalesAsync(
            IEnumerable<SalesLineItem> sourceItems,
            string filePath,
            ExportFormat format,
            string periodLabel)
        {
            var items = sourceItems
                .OrderByDescending(item => item.Date)
                .ToList();

            if (items.Count == 0)
            {
                return ExportResult.Fail(format, "Tidak ada data untuk di-export.");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

                switch (format)
                {
                    case ExportFormat.Csv:
                        await File.WriteAllTextAsync(
                            filePath,
                            CsvExportHelper.GenerateSalesCsv(items, periodLabel),
                            new UTF8Encoding(false));
                        break;
                    case ExportFormat.Excel:
                        await Task.Run(() => WriteSalesExcel(filePath, items, periodLabel));
                        break;
                    case ExportFormat.Pdf:
                        await Task.Run(() => WriteSalesPdf(filePath, items, periodLabel));
                        break;
                    default:
                        return ExportResult.Fail(format, "Format export tidak dikenali.");
                }

                return ExportResult.Ok(
                    filePath,
                    items.Count,
                    format,
                    $"{GetFormatLabel(format)} berhasil dibuat ({items.Count:N0} baris).");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Export sales gagal: {ex.Message}",
                    "Export",
                    ex.ToString());

                return ExportResult.Fail(format, ex.Message);
            }
        }

        private static void WriteSalesExcel(string filePath, List<SalesLineItem> items, string periodLabel)
        {
            using var workbook = new XLWorkbook();

            var summary = workbook.Worksheets.Add("Ringkasan");
            summary.Cell("A1").Value = "Laporan Penjualan";
            summary.Cell("A1").Style.Font.Bold = true;
            summary.Cell("A1").Style.Font.FontSize = 16;
            summary.Cell("A3").Value = "Periode";
            summary.Cell("B3").Value = periodLabel;
            summary.Cell("A4").Value = "Tanggal Export";
            summary.Cell("B4").Value = DateTime.Now;
            summary.Cell("A5").Value = "Total Revenue";
            summary.Cell("B5").Value = items.Sum(item => item.Total);
            summary.Cell("A6").Value = "Total Cost";
            summary.Cell("B6").Value = items.Sum(item => item.Cost);
            summary.Cell("A7").Value = "Total Profit Aronium";
            summary.Cell("B7").Value = items.Sum(item => item.Profit);
            summary.Cell("A8").Value = "Margin";
            decimal totalRevenue = items.Sum(item => item.Total);
            decimal totalProfit = items.Sum(item => item.Profit);
            summary.Cell("B8").Value = totalRevenue > 0 ? totalProfit / totalRevenue : 0;
            summary.Cell("A9").Value = "Diskon Item";
            summary.Cell("B9").Value = items.Sum(item => item.DiscountAmount);
            summary.Cell("A10").Value = "Diskon Nota";
            summary.Cell("B10").Value = items.Sum(item => item.DocumentDiscountAmount);
            summary.Cell("A11").Value = "Tax";
            summary.Cell("B11").Value = items.Sum(item => item.TaxAmount);
            summary.Cell("A12").Value = "Profit After Tax";
            summary.Cell("B12").Value = items.Sum(item => item.ProfitAfterTax);
            summary.Cell("A13").Value = "Items Sold";
            summary.Cell("B13").Value = items.Sum(item => item.Quantity);
            summary.Cell("A14").Value = "Baris Data";
            summary.Cell("B14").Value = items.Count;
            summary.Range("A3:A14").Style.Font.Bold = true;
            summary.Range("B4:B7").Style.NumberFormat.Format = "[$Rp-421] #,##0";
            summary.Range("B8").Style.NumberFormat.Format = "0.00%";
            summary.Range("B9:B12").Style.NumberFormat.Format = "[$Rp-421] #,##0";
            summary.Columns().AdjustToContents();

            var detail = workbook.Worksheets.Add("Detail");
            string[] headers = { "No", "Tanggal", "Invoice", "Produk", "Qty", "Harga", "Cost", "Gross Total", "Diskon Item", "Diskon Nota", "Tax", "Total Setelah Diskon", "Profit Aronium", "Margin %", "Profit After Tax" };
            for (int i = 0; i < headers.Length; i++)
            {
                detail.Cell(1, i + 1).Value = headers[i];
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int row = i + 2;
                detail.Cell(row, 1).Value = i + 1;
                detail.Cell(row, 2).Value = item.Date;
                detail.Cell(row, 3).Value = item.Invoice ?? string.Empty;
                detail.Cell(row, 4).Value = item.ProductName ?? string.Empty;
                detail.Cell(row, 5).Value = item.Quantity;
                detail.Cell(row, 6).Value = item.Price;
                detail.Cell(row, 7).Value = item.Cost;
                detail.Cell(row, 8).Value = item.GrossTotal;
                detail.Cell(row, 9).Value = item.DiscountAmount;
                detail.Cell(row, 10).Value = item.DocumentDiscountAmount;
                detail.Cell(row, 11).Value = item.TaxAmount;
                detail.Cell(row, 12).Value = item.Total;
                detail.Cell(row, 13).Value = item.Profit;
                detail.Cell(row, 14).Value = item.MarginPercent / 100;
                detail.Cell(row, 15).Value = item.ProfitAfterTax;
            }

            var tableRange = detail.Range(1, 1, items.Count + 1, headers.Length);
            tableRange.CreateTable("DetailPenjualan");
            detail.SheetView.FreezeRows(1);
            detail.Column(2).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            detail.Columns(6, 13).Style.NumberFormat.Format = "[$Rp-421] #,##0";
            detail.Column(14).Style.NumberFormat.Format = "0.00%";
            detail.Column(15).Style.NumberFormat.Format = "[$Rp-421] #,##0";
            detail.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        }

        private static void WriteSalesPdf(string filePath, List<SalesLineItem> items, string periodLabel)
        {
            var topItems = items.Take(600).ToList();
            decimal totalRevenue = items.Sum(item => item.Total);
            decimal totalProfit = items.Sum(item => item.Profit);
            decimal totalCost = items.Sum(item => item.Cost);
            decimal totalTax = items.Sum(item => item.TaxAmount);
            decimal itemsSold = items.Sum(item => item.Quantity);
            decimal margin = totalRevenue > 0 ? totalProfit / totalRevenue * 100 : 0;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(text => text.FontSize(9));

                    page.Header().Column(column =>
                    {
                        column.Item().Text("Laporan Penjualan - Smart Sembako Assistant")
                            .SemiBold()
                            .FontSize(16);
                        column.Item().Text($"Periode: {periodLabel} | Export: {DateTime.Now:dd/MM/yyyy HH:mm}");
                    });

                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Row(row =>
                        {
                            AddSummaryCell(row, "Revenue", $"Rp {totalRevenue:N0}");
                            AddSummaryCell(row, "Cost", $"Rp {totalCost:N0}");
                            AddSummaryCell(row, "Profit", $"Rp {totalProfit:N0}");
                            AddSummaryCell(row, "Margin", $"{margin:0.##}%");
                            AddSummaryCell(row, "Tax", $"Rp {totalTax:N0}");
                        });

                        if (items.Count > topItems.Count)
                        {
                            column.Item().Text($"PDF menampilkan {topItems.Count:N0} baris pertama. Gunakan CSV/Excel untuk detail lengkap {items.Count:N0} baris.")
                                .FontColor(Colors.Grey.Darken2);
                        }

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(34);
                                columns.ConstantColumn(72);
                                columns.ConstantColumn(70);
                                columns.RelativeColumn();
                                columns.ConstantColumn(40);
                                columns.ConstantColumn(68);
                                columns.ConstantColumn(68);
                                columns.ConstantColumn(68);
                                columns.ConstantColumn(62);
                                columns.ConstantColumn(62);
                            });

                            AddPdfHeader(table, "#");
                            AddPdfHeader(table, "Tanggal");
                            AddPdfHeader(table, "Invoice");
                            AddPdfHeader(table, "Produk");
                            AddPdfHeader(table, "Qty");
                            AddPdfHeader(table, "Cost");
                            AddPdfHeader(table, "Diskon");
                            AddPdfHeader(table, "Tax");
                            AddPdfHeader(table, "Total");
                            AddPdfHeader(table, "Profit");

                            for (int i = 0; i < topItems.Count; i++)
                            {
                                var item = topItems[i];
                                AddPdfCell(table, (i + 1).ToString());
                                AddPdfCell(table, item.Date.ToString("dd/MM/yyyy"));
                                AddPdfCell(table, item.Invoice ?? string.Empty);
                                AddPdfCell(table, item.ProductName ?? string.Empty);
                                AddPdfCell(table, item.Quantity.ToString("N0"));
                                AddPdfCell(table, $"Rp {item.Cost:N0}");
                                AddPdfCell(table, $"Rp {(item.DiscountAmount + item.DocumentDiscountAmount):N0}");
                                AddPdfCell(table, $"Rp {item.TaxAmount:N0}");
                                AddPdfCell(table, $"Rp {item.Total:N0}");
                                AddPdfCell(table, $"Rp {item.Profit:N0}");
                            }
                        });
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Halaman ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }

        private static void AddSummaryCell(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(column =>
            {
                column.Item().Text(label).FontColor(Colors.Grey.Darken2);
                column.Item().Text(value).SemiBold().FontSize(12);
            });
        }

        private static void AddPdfHeader(TableDescriptor table, string text)
        {
            table.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(text).SemiBold();
        }

        private static void AddPdfCell(TableDescriptor table, string text)
        {
            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(text);
        }

        private static string GetFormatLabel(ExportFormat format)
        {
            return format switch
            {
                ExportFormat.Csv => "CSV",
                ExportFormat.Excel => "Excel",
                ExportFormat.Pdf => "PDF",
                _ => "Export"
            };
        }
    }
}
