namespace SmartSembakoAssistant.Models
{
    public class AppConfig
    {
        public GroqSettings? Groq { get; set; }
        public TelegramSettings? Telegram { get; set; }
        public WhatsAppSettings? WhatsApp { get; set; }
        public PosDbSettings? PosDb { get; set; }
        public GoogleSheetsSettings? GoogleSheets { get; set; }
        public MemorySettings? Memory { get; set; }
        public NotificationSettings? Notifications { get; set; }
        public AppSettings? App { get; set; }
    }

    public class GroqSettings
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; } = "llama-3.3-70b-versatile";
        public string? FallbackApiKey { get; set; } // Gemini
        public string? FallbackModel { get; set; } = "gemini-1.5-flash";
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxTokens { get; set; } = 500; // Reduced from 1000 to avoid rate limit
        public double Temperature { get; set; } = 0.7;
    }

    public class TelegramSettings
    {
        public string? BotToken { get; set; }
        public List<long>? AllowedChatIds { get; set; } // Untuk backward compatibility
        public List<long>? OwnerChatIds { get; set; } // Chat ID Owner (full access)
        public List<long>? KasirChatIds { get; set; } // Chat ID Kasir (restricted access)
        public int RateLimitSeconds { get; set; } = 5;
        public bool EnableVoiceNotes { get; set; } = false;
    }

    public class WhatsAppSettings
    {
        public string? BridgePort { get; set; } = "8080";
        public string? BridgeUrl { get; set; } = "http://localhost:8080/whatsapp";
        public List<string>? OwnerNumbers { get; set; } // WhatsApp numbers Owner
        public List<string>? KasirNumbers { get; set; } // WhatsApp numbers Kasir
    }

    public class PosDbSettings
    {
        public string? DatabasePath { get; set; } = @"C:\Users\{USERNAME}\AppData\Local\Aronium\Data\pos.db";
        public bool AutoDetect { get; set; } = true;
    }

    public class GoogleSheetsSettings
    {
        public string? CredentialsJsonPath { get; set; }
        public string? SpreadsheetId { get; set; }
        public string? TransaksiSheetName { get; set; } = "Transaksi";
        public string? AnalitikSheetName { get; set; } = "Analitik";
        public string? LogSheetName { get; set; } = "Log";
    }

    public class MemorySettings
    {
        public int ShortTermHistoryCount { get; set; } = 5; // Reduced from 10 to save tokens
        public int LongTermSummaryDays { get; set; } = 7;
        public string? DatabasePath { get; set; } = "data\\memory.db";
    }

    public class NotificationSettings
    {
        public List<StockThreshold>? StockThresholds { get; set; }
        public List<ExpiryThreshold>? ExpiryThresholds { get; set; }
        public bool EnableDailySummary { get; set; } = false;
        public string? DailySummaryTime { get; set; } = "08:00";
        public int CheckIntervalMinutes { get; set; } = 5;

        public NotificationSettings()
        {
            StockThresholds = new List<StockThreshold>
            {
                new() { Level = 20, Priority = "Low" },
                new() { Level = 10, Priority = "Medium" },
                new() { Level = 5, Priority = "High" }
            };

            ExpiryThresholds = new List<ExpiryThreshold>
            {
                new() { DaysBefore = 30, Priority = "Warning" },
                new() { DaysBefore = 7, Priority = "Urgent" }
            };
        }
    }

    public class StockThreshold
    {
        public int Level { get; set; }
        public string? Priority { get; set; }
    }

    public class ExpiryThreshold
    {
        public int DaysBefore { get; set; }
        public string? Priority { get; set; }
    }

    public class AppSettings
    {
        public string? Theme { get; set; } = "Light";
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public string? LogPath { get; set; } = "data\\logs";
        public int MaxLogDays { get; set; } = 30;
    }
}
