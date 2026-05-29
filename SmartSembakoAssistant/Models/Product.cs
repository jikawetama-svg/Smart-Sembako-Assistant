namespace SmartSembakoAssistant.Models
{
    /// <summary>
    /// Product model -Mapped dari Aronium schema:
    /// Product Table: Id, Name, Code, Price, Cost, Markup, IsEnabled, MeasurementUnit
    /// Stock Table: Id, ProductId, Quantity (terpisah dari Product)
    /// </summary>
    public class Product
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Sku { get; set; }  // Mapped dari Product.Code
        public string? Category { get; set; } // Mapped dari ProductGroup (via ProductGroupId)
        public decimal? Stock { get; set; } // Mapped dari Stock.Quantity (LEFT JOIN)
        public decimal? PurchasePrice { get; set; } // Mapped dari Product.Cost
        public decimal? SellingPrice { get; set; } // Mapped dari Product.Price
        public decimal? Margin => SellingPrice.HasValue && PurchasePrice.HasValue && PurchasePrice.Value > 0
            ? ((SellingPrice.Value - PurchasePrice.Value) / PurchasePrice.Value) * 100
            : (decimal?)null; // Return null instead of 0 untuk accuracy
        public string? Unit { get; set; } // Mapped dari Product.MeasurementUnit
        public DateTime? ExpiryDate { get; set; } // Mapped dari DocumentItemExpirationDate.ExpirationDate
        public string? BatchNumber { get; set; } // Mapped dari DocumentItemExpirationDate.BatchNumber
        public bool IsActive { get; set; } = true; // Mapped dari Product.IsEnabled
        public decimal SoldThisMonth { get; set; }
        
        // Computed property untuk status stok
        public string StockStatus
        {
            get
            {
                if (!Stock.HasValue || Stock.Value < 0) return "🔴 Minus";
                if (Stock.Value == 0) return "🔴 Habis";
                if (Stock.Value <= 10) return "🟡 Rendah";
                return "🟢 Aman";
            }
        }
        public string StockStatusText
        {
            get
            {
                if (!Stock.HasValue || Stock.Value < 0) return "Minus";
                if (Stock.Value == 0) return "Habis";
                if (Stock.Value <= 10) return "Rendah";
                return "Aman";
            }
        }
    }

    public class Transaction
    {
        public string? Id { get; set; }
        public DateTime? Date { get; set; }
        public string? UserId { get; set; }
        public List<TransactionItem>? Items { get; set; }
        public decimal? Total { get; set; }
        public decimal? Profit { get; set; }
        public string? PaymentMethod { get; set; }
    }

    public class TransactionItem
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? Subtotal { get; set; }
    }

    public class User
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        public string? FullName { get; set; }
        public string? Name { get; set; }
        public string? Role { get; set; } // Owner, Kasir, Admin
        public string? TelegramId { get; set; }
        public string? WhatsappNumber { get; set; }
        public long RoleId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class DocumentInfo
    {
        public string? Id { get; set; }
        public string? Number { get; set; }
        public int DocumentTypeId { get; set; }
        public string? DocumentTypeLabel { get; set; }
        public DateTime? Date { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public decimal Total { get; set; }
    }

    public class DocumentItemInfo
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal ProductCost { get; set; }
        public decimal Total { get; set; }
    }

    public class StockMovement
    {
        public string? Id { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal? Quantity { get; set; }
        public string? Type { get; set; } // In, Out, Adjustment
        public DateTime? Date { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Informasi pelanggan untuk analisis loyalty
    /// </summary>
    public class CustomerInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int PurchaseCount { get; set; }
        public decimal TotalSpent { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
    }

    /// <summary>
    /// Item laporan penjualan per kasir
    /// </summary>
    public class SalesReportItem
    {
        public string? Name { get; set; }
        public int TransactionCount { get; set; }
        public decimal TotalSales { get; set; }
    }

    /// <summary>
    /// Riwayat transaksi pelanggan
    /// </summary>
    public class CustomerTransaction
    {
        public string? TransactionId { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public decimal Total { get; set; }
        public string? ProductName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal ItemTotal { get; set; }
    }

    public class CustomerReceivable
    {
        public string? CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string? Phone { get; set; }
        public int InvoiceCount { get; set; }
        public decimal TotalOwed { get; set; }
        public DateTime? OldestDueDate { get; set; }
        public DateTime? LastTransactionDate { get; set; }
    }

    public class ReceivableInvoice
    {
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal InvoiceTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingBalance { get; set; }
    }

    public class CustomerDocumentSummary
    {
        public string? DocumentId { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public decimal Total { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingBalance { get; set; }
        public int ItemCount { get; set; }
    }

    public class CustomerFavoriteProduct
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
        public int TransactionCount { get; set; }
    }

    /// <summary>
    /// Hasil operasi Restock atau Inventory Count
    /// </summary>
    public class RestockResult
    {
        public bool Success { get; set; }
        public int DocumentId { get; set; }
        public string? DocumentNumber { get; set; }
        public decimal Total { get; set; }
        public decimal OldStock { get; set; } // Stok sebelum operasi, dari transaksi yang sama
        public decimal NewStock { get; set; } // Stok baru setelah operasi
        public string? Error { get; set; }
    }

    public class BulkDocumentItemInput
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal? CurrentStock { get; set; }
        public string? Unit { get; set; }
    }

    public class BulkDocumentItemResult
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal OldStock { get; set; }
        public decimal NewStock { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Adjustment { get; set; }
        public string? Unit { get; set; }
        public decimal Total { get; set; }
    }

    public class BulkDocumentResult
    {
        public bool Success { get; set; }
        public int DocumentId { get; set; }
        public string? DocumentNumber { get; set; }
        public decimal Total { get; set; }
        public List<BulkDocumentItemResult> Items { get; set; } = new();
        public string? Error { get; set; }
    }

    public class ProductMatchCandidate
    {
        public Product Product { get; set; } = new();
        public int Score { get; set; }
        public bool IsExactMatch { get; set; }
    }

    public class ProductMatchResult
    {
        public Product? BestMatch { get; set; }
        public List<ProductMatchCandidate> Candidates { get; set; } = new();
        public bool IsAmbiguous { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Item riwayat restock
    /// </summary>
    public class RestockHistoryItem
    {
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        public decimal UnitCost
        {
            get => Price;
            set => Price = value;
        }
        public decimal TotalCost
        {
            get => Total;
            set => Total = value;
        }
    }

    /// <summary>
    /// Item riwayat inventory
    /// </summary>
    public class InventoryHistoryItem
    {
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public decimal QuantityChange { get; set; }
        public decimal Adjustment
        {
            get => QuantityChange;
            set => QuantityChange = value;
        }
        public DateTime Timestamp
        {
            get => Date ?? DateTime.Now;
            set => Date = value;
        }
        public decimal OldStock { get; set; }
        public decimal NewStock { get; set; }
        public string Reason { get; set; } = "Inventory Count";
    }

    /// <summary>
    /// Rekomendasi restock otomatis
    /// </summary>
    public class RestockRecommendation
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal CostPrice { get; set; }
        public decimal RecommendedQty { get; set; }
        public decimal AverageSales { get; set; }
        public decimal AverageDailySales7Days { get; set; }
        public decimal AverageDailySales30Days { get; set; }
        public decimal SalesLast7Days { get; set; }
        public decimal SalesLast30Days { get; set; }
        public int DaysSafe { get; set; }
        public string Priority { get; set; } = "LOW";
        public bool RequiresManualReview { get; set; }
    }

    /// <summary>
    /// Data penjualan produk untuk analytics
    /// </summary>
    public class ProductSalesData
    {
        public int Rank { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
        public DateTime? LastSaleDate { get; set; }
    }

    public class SlowMovingProductInsight
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public string? Category { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal AverageCategoryQuantity { get; set; }
        public DateTime? LastSaleDate { get; set; }
        public DateTime? LastRestockDate { get; set; }
        public decimal PercentVsCategory => AverageCategoryQuantity > 0
            ? QuantitySold / AverageCategoryQuantity * 100
            : 0;
    }

    public class StockMovementAnalysisSummary
    {
        public int SlowMovingCount { get; set; }
        public int SlowMovingNegativeStockCount { get; set; }
        public int DeadStockCount { get; set; }
        public int SleepingMandatoryCount { get; set; }
        public int SleepingObatCount { get; set; }
        public int SleepingSembakoCount { get; set; }
        public int SleepingBayiCount { get; set; }
        public int UnmappedLargeUnitCount { get; set; }
    }

    public class PaymentBreakdownItem
    {
        public string PaymentTypeName { get; set; } = "";
        public decimal Amount { get; set; }
        public int TransactionCount { get; set; }
    }

    public class ZReportSummary
    {
        public string? Number { get; set; }
        public decimal Amount { get; set; }
    }

    public class ProfitCalculationSummary
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TransactionCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal MarginPercent => Revenue > 0 ? GrossProfit / Revenue * 100 : 0;
    }

    public class SupplierPurchaseSummary
    {
        public string? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public int PurchaseCount { get; set; }
        public decimal TotalPurchase { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
    }

    public class ProductSalesTransaction
    {
        public string? DocumentId { get; set; }
        public string? DocumentNumber { get; set; }
        public DateTime? Date { get; set; }
        public string? CustomerName { get; set; }
        public string? UserName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal ProductCost { get; set; }
        public decimal Total { get; set; }
        public decimal Profit { get; set; }
    }

    /// <summary>
    /// Informasi pembelian pelanggan untuk analytics
    /// </summary>
    public class CustomerPurchaseInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int PurchaseCount { get; set; }
        public decimal TotalSpent { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
    }

    /// <summary>
    /// Data penjualan harian untuk chart
    /// </summary>
    public class DailySalesData
    {
        public DateTime Date { get; set; }
        public string? DateLabel { get; set; }
        public string? FormattedDate => Date.ToString("dd/MM");
        public int TransactionCount { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public string? FormattedRevenue => $"Rp {Revenue:N0}";
        /// <summary>
        /// Calculated bar width in pixels (set in code-behind based on available width)
        /// </summary>
        public double BarWidth { get; set; }
    }

    public class ZeroCostProductInsight
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal SellingPrice { get; set; }
        public string? Unit { get; set; }
        public string? Category { get; set; }
        public decimal QuantitySold30Days { get; set; }
        public decimal Revenue30Days { get; set; }
        public DateTime? LastSaleDate { get; set; }
    }

    public class ZeroCostExportRow
    {
        public string? ProductName { get; set; }
        public decimal SellingPrice { get; set; }
        public string? Unit { get; set; }
        public string? Category { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public DateTime? LastSaleDate { get; set; }
    }

    /// <summary>
    /// Detail item baris penjualan (per product per invoice) untuk laporan
    /// </summary>
    public class SalesLineItem
    {
        public DateTime Date { get; set; }
        public string? Invoice { get; set; }
        public string? ProductName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        public decimal Profit { get; set; }
    }

    /// <summary>
    /// Log perubahan inventory untuk tracking
    /// </summary>
    public class InventoryLog
    {
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal OldStock { get; set; }
        public decimal NewStock { get; set; }
        public decimal Adjustment { get; set; }
        public string Reason { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Channel { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
