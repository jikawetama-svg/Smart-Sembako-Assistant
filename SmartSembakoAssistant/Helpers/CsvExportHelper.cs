using System.Text;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Helpers
{
    public static class CsvExportHelper
    {
        public static string GenerateCustomerCsv(IEnumerable<CustomerInfo> customers)
        {
            var rows = customers.Select((customer, index) => new[]
            {
                (index + 1).ToString(),
                customer.Name ?? string.Empty,
                customer.Phone ?? string.Empty,
                customer.Email ?? string.Empty,
                customer.PurchaseCount.ToString(),
                customer.TotalSpent.ToString("0.##")
            });

            return BuildCsv(
                new[] { "No", "Nama", "HP", "Email", "Total Transaksi", "Total Belanja" },
                rows);
        }

        public static string GenerateSupplierCsv(IEnumerable<CustomerInfo> suppliers)
        {
            var rows = suppliers.Select((supplier, index) => new[]
            {
                (index + 1).ToString(),
                supplier.Name ?? string.Empty,
                supplier.Phone ?? string.Empty,
                supplier.Email ?? string.Empty
            });

            return BuildCsv(
                new[] { "No", "Nama", "HP", "Email" },
                rows);
        }

        public static string GenerateSalesCsv(IEnumerable<SalesLineItem> items, string periodLabel)
        {
            var rows = items.Select((item, index) => new[]
            {
                (index + 1).ToString(),
                item.Date.ToString("yyyy-MM-dd HH:mm:ss"),
                item.Invoice ?? string.Empty,
                item.ProductName ?? string.Empty,
                item.Quantity.ToString("0.##"),
                item.Price.ToString("0.##"),
                item.Total.ToString("0.##"),
                item.Profit.ToString("0.##"),
                periodLabel
            });

            return BuildCsv(
                new[] { "No", "Tanggal", "Dokumen", "Produk", "Qty", "Harga", "Total", "Profit", "Periode" },
                rows);
        }

        public static string GenerateStockCsv(IEnumerable<Product> products)
        {
            var rows = products.Select((product, index) => new[]
            {
                (index + 1).ToString(),
                product.Sku ?? string.Empty,
                product.Name ?? string.Empty,
                (product.Stock ?? 0).ToString("0.##"),
                product.Unit ?? "Pcs",
                (product.PurchasePrice ?? 0).ToString("0.##"),
                (product.SellingPrice ?? 0).ToString("0.##"),
                ((product.Stock ?? 0) * (product.PurchasePrice ?? 0)).ToString("0.##"),
                product.Category ?? string.Empty
            });

            return BuildCsv(
                new[] { "No", "Kode", "Nama", "Stok", "Satuan", "Harga Beli", "Harga Jual", "Nilai Stok", "Kategori" },
                rows);
        }

        public static string GenerateZeroCostCsv(IEnumerable<Product> products)
        {
            var rows = products
                .Where(product => (product.PurchasePrice ?? 0) == 0)
                .Select((product, index) => new[]
                {
                    (index + 1).ToString(),
                    product.Sku ?? string.Empty,
                    product.Name ?? string.Empty,
                    (product.Stock ?? 0).ToString("0.##"),
                    product.Unit ?? "Pcs",
                    (product.SellingPrice ?? 0).ToString("0.##"),
                    product.Category ?? string.Empty
                });

            return BuildCsv(
                new[] { "No", "Kode", "Nama Produk", "Stok", "Satuan", "Harga Jual", "Kategori" },
                rows);
        }

        public static string GenerateZeroCostTierACsv(IEnumerable<ZeroCostProductInsight> products)
        {
            var rows = products.Select((product, index) => new[]
            {
                (index + 1).ToString(),
                product.ProductName ?? string.Empty,
                product.SellingPrice.ToString("0.##"),
                product.Unit ?? "Pcs",
                product.Category ?? string.Empty,
                product.QuantitySold30Days.ToString("0.##"),
                product.Revenue30Days.ToString("0"),
                product.LastSaleDate?.ToString("yyyy-MM-dd") ?? string.Empty
            });

            return BuildCsv(
                new[] { "Prioritas", "Nama Produk", "Harga Jual", "Satuan", "Grup", "Qty Terjual 30hr", "Revenue 30hr", "Terakhir Laku" },
                rows);
        }

        public static string GenerateZeroCostAllCsv(IEnumerable<ZeroCostExportRow> products)
        {
            var rows = products.Select(product => new[]
            {
                product.ProductName ?? string.Empty,
                product.SellingPrice.ToString("0.##"),
                product.Unit ?? "Pcs",
                product.Category ?? string.Empty,
                product.QuantitySold.ToString("0.##"),
                product.LastSaleDate?.ToString("yyyy-MM-dd") ?? string.Empty
            });

            return BuildCsv(
                new[] { "Nama Produk", "Harga Jual", "Satuan", "Grup", "Qty Terjual Total", "Terakhir Laku" },
                rows);
        }

        public static string GenerateReceivableCsv(IEnumerable<CustomerReceivable> receivables)
        {
            var rows = receivables.Select((item, index) => new[]
            {
                (index + 1).ToString(),
                item.CustomerName,
                item.Phone ?? string.Empty,
                item.InvoiceCount.ToString(),
                item.TotalOwed.ToString("0.##"),
                item.OldestDueDate?.ToString("yyyy-MM-dd") ?? string.Empty
            });

            return BuildCsv(
                new[] { "No", "Nama Pelanggan", "HP", "Jumlah Faktur", "Total Hutang", "Jatuh Tempo Tertua" },
                rows);
        }

        private static string BuildCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine(string.Join(",", headers.Select(Escape)));

            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", row.Select(Escape)));
            }

            return sb.ToString();
        }

        private static string Escape(string value)
        {
            string sanitized = value.Replace("\"", "\"\"");
            return $"\"{sanitized}\"";
        }
    }
}
