using Newtonsoft.Json;

namespace SmartSembakoAssistant.Models
{
    public class AppConfig
    {
        public GroqSettings? Groq { get; set; }
        public TelegramSettings? Telegram { get; set; }
        public WhatsAppSettings? WhatsApp { get; set; }
        public BaileysSettings? Baileys { get; set; }
        public TunnelSettings? Tunnel { get; set; }
        public AutomationSettings? Automation { get; set; }
        public PosDbSettings? PosDb { get; set; }
        public GoogleSheetsSettings? GoogleSheets { get; set; }
        public SupabaseSettings? Supabase { get; set; }
        public OcrReceiptSettings? OcrReceipt { get; set; }
        public MappingPolicySettings? MappingPolicy { get; set; }
        public MemorySettings? Memory { get; set; }
        public NotificationSettings? Notifications { get; set; }
        public AppSettings? App { get; set; }
        public AppSetupState? Setup { get; set; }
    }

    public class GroqSettings
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; } = "llama-3.3-70b-versatile";
        public string? FallbackApiKey { get; set; } // Gemini
        public string? FallbackModel { get; set; } = "gemini-2.5-flash";
        public string? VisionModel { get; set; } = "gemini-2.5-flash";
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
        public string? SecretToken { get; set; } = "smart-sembako-secret-token";
    }

    public class WhatsAppSettings
    {
        public bool Enabled { get; set; } = false;
        public string? Mode { get; set; } = WhatsAppModes.CloudApi;
        public string? AccessToken { get; set; }
        public string? PhoneNumberId { get; set; }
        public string? AppSecret { get; set; }
        public string? VerifyToken { get; set; } = "ssa-verify-token";
        public string? GraphApiVersion { get; set; } = "v22.0";
        public int LocalWebhookPort { get; set; } = 8090;
        public string? PublicWebhookUrl { get; set; }
        public bool EnableTemplateMessages { get; set; } = false;
        public string? DefaultTemplateLanguageCode { get; set; } = "id";
        public List<WhatsAppTemplateMapping>? TemplateMappings { get; set; }
        public int OutboundMaxRetries { get; set; } = 5;
        public int InitialRetryDelaySeconds { get; set; } = 15;
        public List<string>? OwnerNumbers { get; set; } // WhatsApp numbers Owner
        public List<string>? KasirNumbers { get; set; } // WhatsApp numbers Kasir
    }

    public class WhatsAppTemplateMapping
    {
        public string Key { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public string? LanguageCode { get; set; }
        public int BodyParameterCount { get; set; } = 1;
    }

    public class BaileysSettings
    {
        public bool Enabled { get; set; } = false;
        public string? BotPhoneNumber { get; set; }
        public string? NodeBinaryPath { get; set; } = "runtimes\\node\\node.exe";
        public string? SidecarEntryPath { get; set; } = "Integrations\\BaileysSidecar\\index.js";
        public string? WorkingDirectory { get; set; } = "Integrations\\BaileysSidecar";
        public string? SessionPath { get; set; } = "data\\baileys-session";
        public int LocalApiPort { get; set; } = 8091;
        public bool AutoStart { get; set; } = true;
        /// <summary>Jeda minimum antar pesan Baileys dalam milidetik.</summary>
        public int MessageDelayMinMs { get; set; } = 1500;
        /// <summary>Jeda maksimum antar pesan Baileys dalam milidetik.</summary>
        public int MessageDelayMaxMs { get; set; } = 3500;
        /// <summary>Batas maksimal pesan Baileys per menit.</summary>
        public int MaxMessagesPerMinute { get; set; } = 20;
        /// <summary>Masa aktif kode pairing dalam detik.</summary>
        public int PairingCodeTtlSeconds { get; set; } = 120;
        /// <summary>Cooldown generate kode pairing baru dalam detik.</summary>
        public int PairingRetryCooldownSeconds { get; set; } = 30;
        /// <summary>Cooldown saat pairing terkena rate limit dalam menit.</summary>
        public int PairingRateLimitCooldownMinutes { get; set; } = 2;
        /// <summary>Batas request kode pairing per jam.</summary>
        public int MaxPairingRequestsPerHour { get; set; } = 8;
        /// <summary>Reset sesi otomatis saat pairing gagal. Default false agar sesi stabil.</summary>
        public bool AutoResetSessionOnPairingFailure { get; set; } = false;
        public List<string>? OwnerNumbers { get; set; }
        public List<string>? KasirNumbers { get; set; }
    }

    public static class WhatsAppModes
    {
        public const string CloudApi = "CloudApi";
        public const string Baileys = "Baileys";
        public const string Both = "Both";

        public static bool UsesCloudApi(string? mode)
        {
            return string.Equals(Normalize(mode), CloudApi, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Normalize(mode), Both, StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesBaileys(string? mode)
        {
            return string.Equals(Normalize(mode), Baileys, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Normalize(mode), Both, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string? mode)
        {
            if (string.Equals(mode, Baileys, StringComparison.OrdinalIgnoreCase))
            {
                return Baileys;
            }

            if (string.Equals(mode, Both, StringComparison.OrdinalIgnoreCase))
            {
                return Both;
            }

            return CloudApi;
        }
    }

    public class TunnelSettings
    {
        public bool Enabled { get; set; } = false;
        public string? Provider { get; set; } = "cloudflared";
        public string? BinaryPath { get; set; } = "runtimes\\cloudflared\\cloudflared.exe";
        public string? ArgsTemplate { get; set; } = "tunnel --url http://localhost:{port} --http-host-header localhost:{port}";
        public string? PublicUrl { get; set; }
    }

    public class AutomationSettings
    {
        public bool EnableTemplates { get; set; } = true;
        public bool EnableLowStockAlerts { get; set; } = false;
        public bool EnableDailySummary { get; set; } = false;
        public bool EnableReceivableAlerts { get; set; } = false;
        public bool EnableExpiryAlerts { get; set; } = false;
        public bool EnableAnomalyAlerts { get; set; } = false;
        public bool EnableDualStockSync { get; set; } = true;
        public bool EnableDualStockRealtimeWatcher { get; set; } = false;
        public int DualStockSyncIntervalSeconds { get; set; } = 15;
        public string? DualStockDailySyncTime { get; set; } = "21:00";
        public string? DailySummaryTime { get; set; } = "21:15";
        public string? LowStockAlertTime { get; set; } = "07:00";
        public string? ReceivableAlertTime { get; set; } = "08:00";
        public string? ExpiryAlertTime { get; set; } = "08:30";
        public string? AnomalyAlertTime { get; set; } = "21:00";
        public bool EnableTelegramLowStockAlerts { get; set; } = false;
        public bool EnableWhatsAppCloudLowStockAlerts { get; set; } = false;
        public bool EnableBaileysLowStockAlerts { get; set; } = false;
        public bool EnableTelegramDailySummaryAlerts { get; set; } = true;
        public bool EnableWhatsAppCloudDailySummaryAlerts { get; set; } = false;
        public bool EnableBaileysDailySummaryAlerts { get; set; } = false;
        public bool EnableWeeklyReport { get; set; } = false;
        public string? WeeklyReportTime { get; set; } = "07:00";
        public bool EnableTelegramWeeklyReportAlerts { get; set; } = true;
        public bool EnableWhatsAppCloudWeeklyReportAlerts { get; set; } = false;
        public bool EnableBaileysWeeklyReportAlerts { get; set; } = false;
        public bool EnableAIReportNarrative { get; set; } = false;
        public bool EnableTelegramDualStockAlerts { get; set; } = true;
        public bool EnableWhatsAppCloudDualStockAlerts { get; set; } = false;
        public bool EnableBaileysDualStockAlerts { get; set; } = false;
        public List<AutomationTemplate>? Templates { get; set; }
        public List<AutomationRule>? Rules { get; set; }
    }

    public class PosDbSettings
    {
        public string? DatabasePath { get; set; } = @"C:\Users\{USERNAME}\AppData\Local\Aronium\Data\pos.db";
        public bool AutoDetect { get; set; } = true;
    }

    public class GoogleSheetsSettings
    {
        public bool Enabled { get; set; } = false;
        public string? CredentialsJsonPath { get; set; }
        public string? SpreadsheetId { get; set; }
        public string? TransaksiSheetName { get; set; } = "Transaksi";
        public string? AnalitikSheetName { get; set; } = "Analitik";
        public string? LogSheetName { get; set; } = "Log";
        /// <summary>Nama tab sheet untuk data pembelian dari OCR struk.</summary>
        public string? PurchaseSheetName { get; set; } = "Pembelian";
        public bool EnableFormatting { get; set; } = true;
        public bool EnableCharts { get; set; } = false;
        public bool EnableConditionalFormatting { get; set; } = true;
    }

    public class SupabaseSettings
    {
        public bool Enabled { get; set; } = false;
        public string? Url { get; set; } = "https://your-project.supabase.co";
        public string? ApiKey { get; set; } = "YOUR_SUPABASE_ANON_OR_SERVICE_ROLE_KEY";
        public string? JwtToken { get; set; }
        public int SyncIntervalMinutes { get; set; } = 15;
        /// <summary>ID tenant permanen untuk satu toko/merchant. Semua perangkat toko yang sama memakai nilai ini.</summary>
        public string? MerchantId { get; set; }
        /// <summary>ID unik instalasi/perangkat untuk audit dan diagnosis sinkronisasi.</summary>
        public string? DeviceId { get; set; }
        /// <summary>Menambahkan merchant_id ke semua payload cloud. MerchantId dan DeviceId dibuat otomatis jika kosong.</summary>
        public bool EnforceTenantIsolation { get; set; } = false;
        /// <summary>primary boleh mengirim snapshot POS; read_only hanya melihat cloud untuk mencegah last-writer-wins antar perangkat.</summary>
        public string SyncMode { get; set; } = "primary";
    }

    /// <summary>Konfigurasi fitur OCR struk pembelian via Telegram.</summary>
    public class OcrReceiptSettings
    {
        public bool Enabled { get; set; } = false;
        /// <summary>Path ke folder tessdata berisi file .traineddata (ind.traineddata wajib ada).</summary>
        public string TessdataPath { get; set; } = "tessdata";
        /// <summary>Caption yang harus disertakan pada foto struk agar diproses OCR.</summary>
        public string TriggerCaption { get; set; } = "/struk";
        /// <summary>Command trigger untuk input receipt berbasis teks.</summary>
        public string? TextTriggerCaption { get; set; } = "/inputstruk";
        /// <summary>Auto-detect pesan teks receipt tanpa command eksplisit.</summary>
        public bool AutoDetectTextReceipt { get; set; } = false;
        /// <summary>Daftar mapping nama produk di faktur/struk supplier ke nama produk di database Aronium.</summary>
        [JsonIgnore]
        public List<OcrProductMapping> ProductMappings { get; set; } = new();
    }

    /// <summary>Pemetaan satu nama produk di faktur supplier ke satu produk di database Aronium.</summary>
    public class OcrProductMapping
    {
        /// <summary>Supplier/vendor pemilik mapping. GLOBAL dipakai untuk mapping lama yang belum punya supplier.</summary>
        public string SupplierKey { get; set; } = "GLOBAL";
        /// <summary>Nama produk seperti yang tertulis di faktur/struk supplier (bisa substring).</summary>
        public string InvoiceName { get; set; } = "";
        /// <summary>Nama invoice yang sudah dinormalisasi untuk trace dan lookup.</summary>
        public string? NormalizedInvoiceName { get; set; }
        /// <summary>ID produk di database Aronium (tabel Product.Id).</summary>
        public string DatabaseProductId { get; set; } = "";
        /// <summary>Nama produk di database Aronium (untuk tampilan UI dan konfirmasi).</summary>
        public string DatabaseProductName { get; set; } = "";
        /// <summary>Sumber mapping: manual, config-mapping, review-queue, auto-match, legacy-alias.</summary>
        public string Source { get; set; } = "manual";
        /// <summary>Status trust mapping: trusted, candidate, blocked, legacy.</summary>
        public string TrustLevel { get; set; } = "trusted";
        public decimal? Confidence { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastConfirmedAt { get; set; }
        public string? Note { get; set; }
    }

    public class OcrMappingsDocument
    {
        public int SchemaVersion { get; set; } = 2;
        public List<OcrSupplierMappings> Suppliers { get; set; } = new();
    }

    public class OcrSupplierMappings
    {
        public string SupplierKey { get; set; } = "GLOBAL";
        public List<string> SupplierNames { get; set; } = new();
        public List<OcrProductMapping> Mappings { get; set; } = new();
    }

    public class MappingPolicySettings
    {
        public bool IncludeDisabledProductsInMappingSearch { get; set; } = true;
        public bool AllowDisabledProductsInRuntimeMappings { get; set; } = true;
        public bool AllowDisabledProductsInAutoDiscovery { get; set; } = false;
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
        [Obsolete("Runtime laporan harian memakai AutomationSettings.DailySummaryTime.")]
        public string? DailySummaryTime { get; set; } = "08:00";
        public int CheckIntervalMinutes { get; set; } = 5;

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
        public string? InstanceId { get; set; }
        public string? MachineName { get; set; }
        public bool IsActiveBotRuntime { get; set; } = true;
        public DateTime? ActiveRuntimeSince { get; set; }
        /// <summary>URL publik Cloud Bot (Render). Digunakan untuk mengirim sinyal failover.</summary>
        public string? CloudBotUrl { get; set; } = "https://smart-sembako-backend.onrender.com";
    }

    public class AppSetupState
    {
        public bool IsFirstRun { get; set; } = true;
        public bool SetupCompleted { get; set; }
        public string? PreferredChannelMode { get; set; } = "WhatsAppOnly";
        public string? LastReadinessStatus { get; set; }
        public DateTime? LastAutoRepairAt { get; set; }
    }
}
