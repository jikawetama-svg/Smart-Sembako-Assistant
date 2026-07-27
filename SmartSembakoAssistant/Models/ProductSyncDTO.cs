using System;
using Newtonsoft.Json;

namespace SmartSembakoAssistant.Models
{
    /// <summary>
    /// Data Transfer Object untuk sinkronisasi delta produk dari C# POS Desktop ke Cloud (Supabase/Firebase).
    /// </summary>
    public class ProductSyncDTO
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("stock")]
        public decimal Stock { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; } = "pcs";

        [JsonProperty("selling_price")]
        public decimal SellingPrice { get; set; }

        [JsonProperty("is_low_stock")]
        public bool IsLowStock { get; set; }

        [JsonProperty("category_name")]
        public string? CategoryName { get; set; }

        [JsonProperty("barcode")]
        public string? Barcode { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Data Transfer Object untuk sinkronisasi agregat transaksi harian ke Cloud.
    /// </summary>
    public class TransactionSummaryDTO
    {
        [JsonProperty("date")]
        public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

        [JsonProperty("total_revenue")]
        public decimal TotalRevenue { get; set; }

        [JsonProperty("total_profit")]
        public decimal TotalProfit { get; set; }

        [JsonProperty("total_transactions")]
        public int TotalTransactions { get; set; }

        [JsonProperty("top_products_json")]
        public string? TopProductsJson { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }
}
