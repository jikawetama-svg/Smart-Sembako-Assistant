using System;
using Newtonsoft.Json;

namespace SmartSembakoAssistant.Models
{
    /// <summary>
    /// Data Transfer Object untuk sinkronisasi delta produk dari C# POS Desktop ke Cloud (Supabase/Firebase).
    /// </summary>
    public class ProductSyncDTO
    {
        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

        [JsonProperty("source_product_id")]
        public string? SourceProductId { get; set; }

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
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

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

    /// <summary>
    /// Data Transfer Object untuk sinkronisasi riwayat restock ke Cloud.
    /// </summary>
    public class RestockSyncDTO
    {
        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

        [JsonProperty("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; } = "pcs";

        [JsonProperty("supplier_name")]
        public string? SupplierName { get; set; }

        [JsonProperty("purchase_price")]
        public decimal PurchasePrice { get; set; }

        [JsonProperty("restock_date")]
        public string? RestockDate { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Data Transfer Object untuk sinkronisasi riwayat koreksi stok ke Cloud.
    /// </summary>
    public class InventorySyncDTO
    {
        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

        [JsonProperty("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [JsonProperty("quantity_before")]
        public decimal QuantityBefore { get; set; }

        [JsonProperty("quantity_after")]
        public decimal QuantityAfter { get; set; }

        [JsonProperty("delta")]
        public decimal Delta { get; set; }

        [JsonProperty("reason")]
        public string? Reason { get; set; }

        [JsonProperty("corrected_at")]
        public string? CorrectedAt { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Data Transfer Object untuk sinkronisasi ringkas pelanggan & piutang ke Cloud.
    /// </summary>
    public class CustomerSyncDTO
    {
        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("phone")]
        public string? Phone { get; set; }

        [JsonProperty("total_debt")]
        public decimal TotalDebt { get; set; }

        [JsonProperty("last_transaction_date")]
        public DateTime? LastTransactionDate { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Data Transfer Object untuk sinkronisasi supplier ke Cloud.
    /// </summary>
    public class SupplierSyncDTO
    {
        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_device_id")]
        public string? SourceDeviceId { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("phone")]
        public string? Phone { get; set; }

        [JsonProperty("email")]
        public string? Email { get; set; }

        [JsonProperty("address")]
        public string? Address { get; set; }

        [JsonProperty("synced_at")]
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Perintah dari Cloud Bot yang harus dieksekusi Desktop lokal.
    /// </summary>
    public class AgentCommandQueueItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonProperty("source_channel")]
        public string SourceChannel { get; set; } = "telegram";

        [JsonProperty("source_chat_id")]
        public string SourceChatId { get; set; } = string.Empty;

        [JsonProperty("source_user_id")]
        public string? SourceUserId { get; set; }

        [JsonProperty("command_text")]
        public string CommandText { get; set; } = string.Empty;

        [JsonProperty("command_kind")]
        public string CommandKind { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = "pending";

        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}
