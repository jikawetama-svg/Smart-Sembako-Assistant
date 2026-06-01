namespace SmartSembakoAssistant.Models
{
    public static class GoogleSheetsSchema
    {
        public static readonly IReadOnlyList<string> PurchaseHeaders = new[]
        {
            "Tanggal",
            "No Dokumen",
            "Supplier",
            "Produk",
            "Nama OCR Asli",
            "Mapping Source",
            "Trust Level",
            "Qty",
            "Satuan",
            "Harga Satuan",
            "Total",
            "Status",
            "ProductId",
            "LineIndex",
            "RowKey",
            "OldStock",
            "NewStock",
            "Source",
            "CorrelationId",
            "SyncedAt"
        };
    }
}
