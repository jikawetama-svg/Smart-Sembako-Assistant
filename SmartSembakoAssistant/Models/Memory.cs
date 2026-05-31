namespace SmartSembakoAssistant.Models
{
    public class Conversation
    {
        public long Id { get; set; }
        public long ChatId { get; set; }
        public string? UserName { get; set; }
        public string? Role { get; set; } // user, assistant, system
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? MessageType { get; set; } // text, command, photo, voice
    }

    public class LongTermMemory
    {
        public long Id { get; set; }
        public string? Category { get; set; } // habits, preferences, patterns
        public string? Summary { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public int UsageCount { get; set; } = 1;
    }

    public class LogEntry
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Level { get; set; } // Info, Warning, Error, Critical
        public string? Category { get; set; } // Command, OCR, AI, Notification, Anomaly, System
        public string? Message { get; set; }
        public string? Details { get; set; }
        public string? UserId { get; set; }
    }

    public class LogDeleteResult
    {
        public int DeletedCount { get; set; }
        public int BatchCount { get; set; }
    }

    public class OcrResult
    {
        public string? ImagePath { get; set; }
        public string? RawText { get; set; }
        public ParsedReceipt? ParsedData { get; set; }
        public bool IsConfirmed { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ParsedReceipt
    {
        public string? StoreName { get; set; }
        public string? SupplierName { get; set; }
        public string? BuyerName { get; set; }
        public string? VendorType { get; set; }
        public DateTime? Date { get; set; }
        public string? RawDateText { get; set; }
        public string? ReceiptNumber { get; set; }
        public bool IsLastPage { get; set; }
        public List<ReceiptItem>? Items { get; set; }
        public decimal? Total { get; set; }
        public decimal? Tax { get; set; }
        public string? PaymentMethod { get; set; }
    }

    public class ReceiptItem
    {
        public string? ProductName { get; set; }
        public decimal? QtyBox { get; set; }
        public int? IsiPerBox { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Total { get; set; }
    }
}
