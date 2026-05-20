using System.IO;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public sealed class SetupReadinessService
    {
        private readonly ConfigService _configService;

        public SetupReadinessService(ConfigService configService)
        {
            _configService = configService;
        }

        public void SeedDefaults()
        {
            var config = _configService.Config ?? new AppConfig();
            config.Groq ??= new GroqSettings();
            config.Telegram ??= new TelegramSettings();
            config.WhatsApp ??= new WhatsAppSettings();
            config.Baileys ??= new BaileysSettings();
            config.Tunnel ??= new TunnelSettings();
            config.Automation ??= new AutomationSettings();
            config.PosDb ??= new PosDbSettings();
            config.GoogleSheets ??= new GoogleSheetsSettings();
            config.OcrReceipt ??= new OcrReceiptSettings();
            config.Memory ??= new MemorySettings();
            config.Notifications ??= new NotificationSettings();
            config.App ??= new AppSettings();
            config.Setup ??= new AppSetupState();
            config.OcrReceipt.ProductMappings ??= new List<OcrProductMapping>();

            SeedNotificationThresholdDefaults(config.Notifications);

            config.WhatsApp.Mode = string.IsNullOrWhiteSpace(config.WhatsApp.Mode)
                ? WhatsAppModes.Baileys
                : WhatsAppModes.Normalize(config.WhatsApp.Mode);
            config.WhatsApp.VerifyToken = string.IsNullOrWhiteSpace(config.WhatsApp.VerifyToken)
                ? $"ssa-{Guid.NewGuid():N}".Substring(0, 12)
                : config.WhatsApp.VerifyToken;

            config.Baileys.NodeBinaryPath = string.IsNullOrWhiteSpace(config.Baileys.NodeBinaryPath)
                ? GetDefaultNodeBinaryPath()
                : config.Baileys.NodeBinaryPath;
            config.Baileys.SidecarEntryPath = string.IsNullOrWhiteSpace(config.Baileys.SidecarEntryPath)
                ? "Integrations\\BaileysSidecar\\index.js"
                : config.Baileys.SidecarEntryPath;
            config.Baileys.WorkingDirectory = string.IsNullOrWhiteSpace(config.Baileys.WorkingDirectory)
                ? "Integrations\\BaileysSidecar"
                : config.Baileys.WorkingDirectory;
            config.Baileys.SessionPath = string.IsNullOrWhiteSpace(config.Baileys.SessionPath)
                ? "data\\baileys-session"
                : config.Baileys.SessionPath;
            config.Baileys.LocalApiPort = config.Baileys.LocalApiPort <= 0 ? 8091 : config.Baileys.LocalApiPort;
            config.Baileys.AutoStart = true;
            config.Baileys.MessageDelayMinMs = config.Baileys.MessageDelayMinMs <= 0 ? 1500 : config.Baileys.MessageDelayMinMs;
            config.Baileys.MessageDelayMaxMs = config.Baileys.MessageDelayMaxMs < config.Baileys.MessageDelayMinMs ? 3500 : config.Baileys.MessageDelayMaxMs;
            config.Baileys.MaxMessagesPerMinute = config.Baileys.MaxMessagesPerMinute <= 0 ? 20 : config.Baileys.MaxMessagesPerMinute;
            config.Baileys.PairingCodeTtlSeconds = config.Baileys.PairingCodeTtlSeconds <= 0 ? 120 : config.Baileys.PairingCodeTtlSeconds;
            config.Baileys.PairingRetryCooldownSeconds = config.Baileys.PairingRetryCooldownSeconds <= 0 ? 30 : config.Baileys.PairingRetryCooldownSeconds;
            config.Baileys.PairingRateLimitCooldownMinutes = config.Baileys.PairingRateLimitCooldownMinutes <= 0 ? 2 : config.Baileys.PairingRateLimitCooldownMinutes;
            config.Baileys.MaxPairingRequestsPerHour = config.Baileys.MaxPairingRequestsPerHour <= 0 ? 8 : config.Baileys.MaxPairingRequestsPerHour;

            config.Tunnel.Provider = string.IsNullOrWhiteSpace(config.Tunnel.Provider) ? "cloudflared" : config.Tunnel.Provider;
            config.Tunnel.BinaryPath = string.IsNullOrWhiteSpace(config.Tunnel.BinaryPath)
                ? "runtimes\\cloudflared\\cloudflared.exe"
                : config.Tunnel.BinaryPath;

            config.WhatsApp.LocalWebhookPort = config.WhatsApp.LocalWebhookPort <= 0 ? 8090 : config.WhatsApp.LocalWebhookPort;
            config.WhatsApp.OutboundMaxRetries = config.WhatsApp.OutboundMaxRetries <= 0 ? 5 : config.WhatsApp.OutboundMaxRetries;
            config.WhatsApp.InitialRetryDelaySeconds = config.WhatsApp.InitialRetryDelaySeconds <= 0 ? 15 : config.WhatsApp.InitialRetryDelaySeconds;

            if (config.PosDb.AutoDetect && string.IsNullOrWhiteSpace(config.PosDb.DatabasePath))
            {
                config.PosDb.DatabasePath = PosDbService.AutoDetectPosDbPath() ?? config.PosDb.DatabasePath;
            }

            _configService.ReplaceInMemoryConfig(config, save: false);
        }

        public bool ShouldShowWizard()
        {
            SeedDefaults();
            var config = _configService.Config ?? new AppConfig();
            string groqKey = config.Groq?.ApiKey ?? string.Empty;
            string waMode = WhatsAppModes.Normalize(config.WhatsApp?.Mode);

            bool groqReady = !string.IsNullOrWhiteSpace(groqKey) && groqKey != "YOUR_GROQ_API_KEY";
            bool baileysReady = config.Baileys?.Enabled == true &&
                                WhatsAppModes.UsesBaileys(waMode) &&
                                !string.IsNullOrWhiteSpace(config.Baileys.BotPhoneNumber) &&
                                (config.Baileys.OwnerNumbers?.Any() == true);
            bool telegramChosen = TelegramBotService.IsBotTokenFormatValid(config.Telegram?.BotToken);
            bool cloudReady = config.WhatsApp?.Enabled == true &&
                              WhatsAppModes.UsesCloudApi(waMode) &&
                              !string.IsNullOrWhiteSpace(config.WhatsApp.AccessToken) &&
                              !string.IsNullOrWhiteSpace(config.WhatsApp.PhoneNumberId) &&
                              (config.WhatsApp.OwnerNumbers?.Any() == true);

            bool complete = groqReady && (baileysReady || telegramChosen || cloudReady);
            config.Setup ??= new AppSetupState();
            config.Setup.LastReadinessStatus = complete ? "ready" : "needs_setup";
            _configService.ReplaceInMemoryConfig(config, save: false);
            return config.Setup.SetupCompleted != true || !complete;
        }

        public void ApplyBasicSetup(
            string channelMode,
            string ownerPhoneNumber,
            string groqApiKey,
            string? botPhoneNumber = null,
            string? telegramToken = null)
        {
            SeedDefaults();
            var config = _configService.Config ?? new AppConfig();

            string normalizedOwner = AutomationEngine.NormalizeWhatsAppNumber(ownerPhoneNumber);
            string normalizedBot = AutomationEngine.NormalizeWhatsAppNumber(botPhoneNumber);
            bool useWhatsApp = !string.Equals(channelMode, "TelegramOnly", StringComparison.OrdinalIgnoreCase);
            bool useTelegram = !string.Equals(channelMode, "WhatsAppOnly", StringComparison.OrdinalIgnoreCase);

            config.Groq!.ApiKey = groqApiKey.Trim();
            config.Groq.Model ??= "llama-3.3-70b-versatile";

            config.Setup ??= new AppSetupState();
            config.Setup.IsFirstRun = false;
            config.Setup.SetupCompleted = true;
            config.Setup.PreferredChannelMode = channelMode;
            config.Setup.LastReadinessStatus = "configured";

            config.WhatsApp ??= new WhatsAppSettings();
            config.Baileys ??= new BaileysSettings();
            config.Telegram ??= new TelegramSettings();

            config.WhatsApp.Enabled = useWhatsApp;
            config.WhatsApp.Mode = useWhatsApp && useTelegram ? WhatsAppModes.Baileys : useWhatsApp ? WhatsAppModes.Baileys : config.WhatsApp.Mode;
            config.WhatsApp.OwnerNumbers = string.IsNullOrWhiteSpace(normalizedOwner) ? new List<string>() : new List<string> { normalizedOwner };
            config.WhatsApp.KasirNumbers ??= new List<string>();

            config.Baileys.Enabled = useWhatsApp;
            config.Baileys.BotPhoneNumber = string.IsNullOrWhiteSpace(normalizedBot) ? normalizedOwner : normalizedBot;
            config.Baileys.OwnerNumbers = string.IsNullOrWhiteSpace(normalizedOwner) ? new List<string>() : new List<string> { normalizedOwner };
            config.Baileys.KasirNumbers ??= new List<string>();
            config.Baileys.NodeBinaryPath = GetDefaultNodeBinaryPath();

            config.Telegram.BotToken = useTelegram ? telegramToken?.Trim() : string.Empty;
            config.Telegram.OwnerChatIds ??= new List<long>();
            config.Telegram.KasirChatIds ??= new List<long>();
            config.Telegram.AllowedChatIds = config.Telegram.OwnerChatIds
                .Concat(config.Telegram.KasirChatIds ?? new List<long>())
                .Distinct()
                .ToList();

            if (config.PosDb?.AutoDetect == true)
            {
                config.PosDb.DatabasePath = PosDbService.AutoDetectPosDbPath() ?? config.PosDb.DatabasePath;
            }

            _configService.ReplaceInMemoryConfig(config, save: true);
        }

        public string AutoDetectNodeBinaryPath()
        {
            var candidates = new[]
            {
                GetDefaultNodeBinaryPath(),
                "node",
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe")
            };

            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, "node", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "node";
        }

        private static string GetDefaultNodeBinaryPath()
        {
            return "runtimes\\node\\node.exe";
        }

        private static void SeedNotificationThresholdDefaults(NotificationSettings notifications)
        {
            if (notifications.StockThresholds == null)
            {
                notifications.StockThresholds = new List<StockThreshold>
                {
                    new() { Level = 20, Priority = "Low" },
                    new() { Level = 10, Priority = "Medium" },
                    new() { Level = 5, Priority = "High" }
                };
            }

            if (notifications.ExpiryThresholds == null)
            {
                notifications.ExpiryThresholds = new List<ExpiryThreshold>
                {
                    new() { DaysBefore = 30, Priority = "Warning" },
                    new() { DaysBefore = 7, Priority = "Urgent" }
                };
            }
        }
    }
}
