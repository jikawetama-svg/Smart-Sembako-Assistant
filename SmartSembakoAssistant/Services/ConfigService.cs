using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class ConfigService
    {
        private const string OcrMappingsRelativePath = "data\\ocr_mappings.json";
        private readonly string _configPath;
        private readonly string _ocrMappingsPath;
        private readonly LoggingService? _loggingService;
        private AppConfig? _config;

        public AppConfig? Config => _config;
        public string ConfigPath => _configPath;
        public string OcrMappingsPath => _ocrMappingsPath;
        public IReadOnlyList<string> DetectedConfigPaths { get; private set; } = Array.Empty<string>();
        public string? DuplicateConfigWarning { get; private set; }

        public ConfigService(string configPath = "config.json", LoggingService? loggingService = null)
        {
            _configPath = ResolveConfigPath(configPath);
            _ocrMappingsPath = ResolveSiblingPath(_configPath, OcrMappingsRelativePath);
            _loggingService = loggingService;
            AnalyzeConfigCandidates(configPath);
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    string templatePath = ResolveTemplatePath();
                    if (File.Exists(templatePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                        File.Copy(templatePath, _configPath);
                    }
                    else
                    {
                        _config = new AppConfig();
                        SaveConfig();
                        return;
                    }
                }

                string json = File.ReadAllText(_configPath);
                List<OcrProductMapping> legacyOcrMappings = ExtractLegacyOcrMappings(json);
                _config = JsonConvert.DeserializeObject<AppConfig>(json);

                // Decrypt API keys saat load
                if (_config?.Groq != null && !string.IsNullOrEmpty(_config.Groq.ApiKey))
                {
                    _config.Groq.ApiKey = DecryptValue(_config.Groq.ApiKey);
                }
                if (_config?.Groq?.FallbackApiKey != null && !string.IsNullOrEmpty(_config.Groq.FallbackApiKey))
                {
                    _config.Groq.FallbackApiKey = DecryptValue(_config.Groq.FallbackApiKey);
                }
                if (_config?.Telegram != null && !string.IsNullOrEmpty(_config.Telegram.BotToken))
                {
                    _config.Telegram.BotToken = DecryptValue(_config.Telegram.BotToken);
                }
                if (_config?.WhatsApp != null)
                {
                    _config.WhatsApp.AccessToken = DecryptValue(_config.WhatsApp.AccessToken);
                    _config.WhatsApp.AppSecret = DecryptValue(_config.WhatsApp.AppSecret);
                    _config.WhatsApp.VerifyToken = DecryptValue(_config.WhatsApp.VerifyToken);
                }

                LoadOcrMappingsIntoConfig(legacyOcrMappings);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    _loggingService.LogErrorAsync($"Error loading config: {ex.Message}", "Config", ex.ToString());
                }
                _config = new AppConfig();
                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            try
            {
                SaveOcrMappings(_config?.OcrReceipt?.ProductMappings);

                // Buat salinan config untuk di-encrypt (jangan modify original)
                var configCopy = new AppConfig
                {
                    Groq = _config?.Groq != null ? new GroqSettings
                    {
                        ApiKey = EncryptValue(_config.Groq.ApiKey),
                        Model = _config.Groq.Model,
                        FallbackApiKey = EncryptValue(_config.Groq.FallbackApiKey),
                        FallbackModel = _config.Groq.FallbackModel,
                        VisionModel = _config.Groq.VisionModel,
                        TimeoutSeconds = _config.Groq.TimeoutSeconds,
                        MaxTokens = _config.Groq.MaxTokens,
                        Temperature = _config.Groq.Temperature
                    } : null,
                    Telegram = _config?.Telegram != null ? new TelegramSettings
                    {
                        BotToken = EncryptValue(_config.Telegram.BotToken),
                        AllowedChatIds = _config.Telegram.AllowedChatIds?.ToList(),
                        OwnerChatIds = _config.Telegram.OwnerChatIds?.ToList(),
                        KasirChatIds = _config.Telegram.KasirChatIds?.ToList(),
                        RateLimitSeconds = _config.Telegram.RateLimitSeconds,
                        EnableVoiceNotes = _config.Telegram.EnableVoiceNotes
                    } : null,
                    WhatsApp = _config?.WhatsApp != null ? new WhatsAppSettings
                    {
                        Enabled = _config.WhatsApp.Enabled,
                        Mode = _config.WhatsApp.Mode,
                        AccessToken = EncryptValue(_config.WhatsApp.AccessToken),
                        PhoneNumberId = _config.WhatsApp.PhoneNumberId,
                        AppSecret = EncryptValue(_config.WhatsApp.AppSecret),
                        VerifyToken = EncryptValue(_config.WhatsApp.VerifyToken),
                        GraphApiVersion = _config.WhatsApp.GraphApiVersion,
                        LocalWebhookPort = _config.WhatsApp.LocalWebhookPort,
                        PublicWebhookUrl = _config.WhatsApp.PublicWebhookUrl,
                        EnableTemplateMessages = _config.WhatsApp.EnableTemplateMessages,
                        DefaultTemplateLanguageCode = _config.WhatsApp.DefaultTemplateLanguageCode,
                        TemplateMappings = _config.WhatsApp.TemplateMappings?
                            .Select(mapping => new WhatsAppTemplateMapping
                            {
                                Key = mapping.Key,
                                TemplateName = mapping.TemplateName,
                                LanguageCode = mapping.LanguageCode,
                                BodyParameterCount = mapping.BodyParameterCount
                            })
                            .ToList(),
                        OutboundMaxRetries = _config.WhatsApp.OutboundMaxRetries,
                        InitialRetryDelaySeconds = _config.WhatsApp.InitialRetryDelaySeconds,
                        OwnerNumbers = _config.WhatsApp.OwnerNumbers?.ToList(),
                        KasirNumbers = _config.WhatsApp.KasirNumbers?.ToList()
                    } : null,
                    Baileys = _config?.Baileys != null ? new BaileysSettings
                    {
                        Enabled = _config.Baileys.Enabled,
                        BotPhoneNumber = _config.Baileys.BotPhoneNumber,
                        NodeBinaryPath = _config.Baileys.NodeBinaryPath,
                        SidecarEntryPath = _config.Baileys.SidecarEntryPath,
                        WorkingDirectory = _config.Baileys.WorkingDirectory,
                        SessionPath = _config.Baileys.SessionPath,
                        LocalApiPort = _config.Baileys.LocalApiPort,
                        AutoStart = _config.Baileys.AutoStart,
                        MessageDelayMinMs = _config.Baileys.MessageDelayMinMs,
                        MessageDelayMaxMs = _config.Baileys.MessageDelayMaxMs,
                        MaxMessagesPerMinute = _config.Baileys.MaxMessagesPerMinute,
                        PairingCodeTtlSeconds = _config.Baileys.PairingCodeTtlSeconds,
                        PairingRetryCooldownSeconds = _config.Baileys.PairingRetryCooldownSeconds,
                        PairingRateLimitCooldownMinutes = _config.Baileys.PairingRateLimitCooldownMinutes,
                        MaxPairingRequestsPerHour = _config.Baileys.MaxPairingRequestsPerHour,
                        AutoResetSessionOnPairingFailure = _config.Baileys.AutoResetSessionOnPairingFailure,
                        OwnerNumbers = _config.Baileys.OwnerNumbers?.ToList(),
                        KasirNumbers = _config.Baileys.KasirNumbers?.ToList()
                    } : null,
                    Tunnel = _config?.Tunnel,
                    Automation = _config?.Automation,
                    PosDb = _config?.PosDb,
                    GoogleSheets = _config?.GoogleSheets != null ? new GoogleSheetsSettings
                    {
                        Enabled = _config.GoogleSheets.Enabled,
                        CredentialsJsonPath = _config.GoogleSheets.CredentialsJsonPath,
                        SpreadsheetId = _config.GoogleSheets.SpreadsheetId,
                        TransaksiSheetName = _config.GoogleSheets.TransaksiSheetName,
                        AnalitikSheetName = _config.GoogleSheets.AnalitikSheetName,
                        LogSheetName = _config.GoogleSheets.LogSheetName,
                        PurchaseSheetName = _config.GoogleSheets.PurchaseSheetName
                    } : null,
                    OcrReceipt = _config?.OcrReceipt != null ? new OcrReceiptSettings
                    {
                        Enabled = _config.OcrReceipt.Enabled,
                        TessdataPath = _config.OcrReceipt.TessdataPath,
                        TriggerCaption = _config.OcrReceipt.TriggerCaption,
                        TextTriggerCaption = _config.OcrReceipt.TextTriggerCaption,
                        AutoDetectTextReceipt = _config.OcrReceipt.AutoDetectTextReceipt
                    } : null,
                    Memory = _config?.Memory,
                    Notifications = _config?.Notifications != null ? new NotificationSettings
                    {
                        StockThresholds = _config.Notifications.StockThresholds?
                            .Select(threshold => new StockThreshold
                            {
                                Level = threshold.Level,
                                Priority = threshold.Priority
                            }).ToList(),
                        ExpiryThresholds = _config.Notifications.ExpiryThresholds?
                            .Select(threshold => new ExpiryThreshold
                            {
                                DaysBefore = threshold.DaysBefore,
                                Priority = threshold.Priority
                            }).ToList(),
                        EnableDailySummary = _config.Notifications.EnableDailySummary,
                        DailySummaryTime = _config.Notifications.DailySummaryTime,
                        CheckIntervalMinutes = _config.Notifications.CheckIntervalMinutes
                    } : null,
                    App = _config?.App,
                    Setup = _config?.Setup
                };

                string json = JsonConvert.SerializeObject(configCopy, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Gagal menyimpan konfigurasi: {ex.Message}");
            }
        }

        private string DecryptValue(string? encryptedValue)
        {
            if (string.IsNullOrEmpty(encryptedValue))
                return string.Empty;

            // Cek apakah value sudah dalam bentuk plain text (tidak diawali base64 pattern)
            if (!encryptedValue.StartsWith("ENC:"))
            {
                // Sudah plain text (mungkin dari manual edit), kembalikan apa adanya
                return encryptedValue;
            }

            try
            {
                // Hapus prefix "ENC:" dan decrypt
                string pureEncrypted = encryptedValue.Substring(4);
                byte[] decryptedData = ProtectedData.Unprotect(
                    Convert.FromBase64String(pureEncrypted),
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decryptedData);
            }
            catch
            {
                // Jika gagal, kembalikan sebagai plain text (untuk development)
                return encryptedValue;
            }
        }

        private string EncryptValue(string? plainValue)
        {
            if (string.IsNullOrEmpty(plainValue))
                return string.Empty;

            // Jika sudah terenkripsi, kembalikan apa adanya
            if (plainValue.StartsWith("ENC:"))
                return plainValue;

            try
            {
                // DPAPI encrypt dengan prefix "ENC:" untuk menandai encrypted value
                byte[] encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plainValue),
                    null,
                    DataProtectionScope.CurrentUser);

                return "ENC:" + Convert.ToBase64String(encryptedData);
            }
            catch
            {
                // Jika gagal, simpan sebagai plain text (fallback)
                return plainValue;
            }
        }

        public void UpdateGroqSettings(GroqSettings settings)
        {
            if (_config != null)
            {
                _config.Groq = settings;
                SaveConfig();
            }
        }

        public void UpdateTelegramSettings(TelegramSettings settings)
        {
            if (_config != null)
            {
                _config.Telegram = settings;
                SaveConfig();
            }
        }

        public void UpdatePosDbSettings(PosDbSettings settings)
        {
            if (_config != null)
            {
                _config.PosDb = settings;
                SaveConfig();
            }
        }

        public void UpdateGoogleSheetsSettings(GoogleSheetsSettings settings)
        {
            if (_config != null)
            {
                _config.GoogleSheets = settings;
                SaveConfig();
            }
        }

        public void UpdateNotificationSettings(NotificationSettings settings)
        {
            if (_config != null)
            {
                _config.Notifications = settings;
                SaveConfig();
            }
        }

        public void UpdateWhatsAppSettings(WhatsAppSettings settings)
        {
            if (_config != null)
            {
                _config.WhatsApp = settings;
                SaveConfig();
            }
        }

        public void UpdateTunnelSettings(TunnelSettings settings)
        {
            if (_config != null)
            {
                _config.Tunnel = settings;
                SaveConfig();
            }
        }

        public void UpdateBaileysSettings(BaileysSettings settings)
        {
            if (_config != null)
            {
                _config.Baileys = settings;
                SaveConfig();
            }
        }

        public void UpdateAutomationSettings(AutomationSettings settings)
        {
            if (_config != null)
            {
                _config.Automation = settings;
                SaveConfig();
            }
        }

        public void UpdateAppSettings(AppSettings settings)
        {
            if (_config != null)
            {
                _config.App = settings;
                SaveConfig();
            }
        }

        public AppConfig CloneConfig(AppConfig? source = null)
        {
            AppConfig original = source ?? _config ?? new AppConfig();
            string json = JsonConvert.SerializeObject(original);
            AppConfig clone = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

            if (original.OcrReceipt?.ProductMappings != null)
            {
                clone.OcrReceipt ??= new OcrReceiptSettings();
                clone.OcrReceipt.ProductMappings = CloneOcrMappings(original.OcrReceipt.ProductMappings);
            }

            return clone;
        }

        public void ReplaceInMemoryConfig(AppConfig config, bool save = false)
        {
            _config = CloneConfig(config);
            if (save)
            {
                SaveConfig();
            }
        }

        public void AddOcrMapping(string invoiceName, string dbProductId, string dbProductName)
        {
            string normalizedInvoiceName = invoiceName?.Trim() ?? string.Empty;
            string normalizedDbProductId = dbProductId?.Trim() ?? string.Empty;
            string normalizedDbProductName = dbProductName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedInvoiceName) ||
                string.IsNullOrWhiteSpace(normalizedDbProductId) ||
                string.IsNullOrWhiteSpace(normalizedDbProductName))
            {
                return;
            }

            _config ??= new AppConfig();
            _config.OcrReceipt ??= new OcrReceiptSettings();
            _config.OcrReceipt.ProductMappings ??= new List<OcrProductMapping>();

            var existing = _config.OcrReceipt.ProductMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.InvoiceName, normalizedInvoiceName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.InvoiceName = normalizedInvoiceName;
                existing.DatabaseProductId = normalizedDbProductId;
                existing.DatabaseProductName = normalizedDbProductName;
            }
            else
            {
                _config.OcrReceipt.ProductMappings.Add(new OcrProductMapping
                {
                    InvoiceName = normalizedInvoiceName,
                    DatabaseProductId = normalizedDbProductId,
                    DatabaseProductName = normalizedDbProductName
                });
            }

            SaveOcrMappings(_config.OcrReceipt.ProductMappings);
        }

        public void ReplaceOcrMappings(IEnumerable<OcrProductMapping>? mappings)
        {
            _config ??= new AppConfig();
            _config.OcrReceipt ??= new OcrReceiptSettings();
            _config.OcrReceipt.ProductMappings = CloneOcrMappings(mappings);
            SaveOcrMappings(_config.OcrReceipt.ProductMappings);
        }

        public bool IsConfigured()
        {
            string waMode = WhatsAppModes.Normalize(_config?.WhatsApp?.Mode);
            bool groqReady = !string.IsNullOrWhiteSpace(_config?.Groq?.ApiKey) &&
                             _config?.Groq?.ApiKey != "YOUR_GROQ_API_KEY";
            bool telegramReady = !string.IsNullOrWhiteSpace(_config?.Telegram?.BotToken) &&
                                 _config?.Telegram?.BotToken != "YOUR_TELEGRAM_BOT_TOKEN";
            bool baileysReady = _config?.Baileys?.Enabled == true &&
                                WhatsAppModes.UsesBaileys(waMode) &&
                                !string.IsNullOrWhiteSpace(_config.Baileys.BotPhoneNumber) &&
                                (_config.Baileys.OwnerNumbers?.Any() == true);
            bool cloudReady = _config?.WhatsApp?.Enabled == true &&
                              WhatsAppModes.UsesCloudApi(waMode) &&
                              !string.IsNullOrWhiteSpace(_config.WhatsApp.AccessToken) &&
                              !string.IsNullOrWhiteSpace(_config.WhatsApp.PhoneNumberId);

            return _config?.Setup?.SetupCompleted == true &&
                   groqReady &&
                   (telegramReady || baileysReady || cloudReady);
        }

        private static string ResolveConfigPath(string configPath)
        {
            if (Path.IsPathRooted(configPath))
            {
                return configPath;
            }

            var existingCandidates = GetConfigCandidates(configPath);

            string? existing = ChooseBestExistingConfig(existingCandidates);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            string? projectRoot = TryFindProjectRoot();
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                if (RuntimePaths.IsPortableMode)
                {
                    return Path.Combine(projectRoot, configPath);
                }
            }

            return RuntimePaths.ResolveWritablePath(configPath, configPath);
        }

        private static string ResolveTemplatePath()
        {
            string templateFile = "config.template.json";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            string? projectRoot = TryFindProjectRoot();

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                candidates.Add(Path.Combine(projectRoot, templateFile));
            }

            candidates.Add(Path.Combine(RuntimePaths.AppBaseDirectory, templateFile));
            candidates.Add(Path.Combine(baseDir, templateFile));
            candidates.Add(Path.Combine(currentDir, templateFile));

            return candidates.FirstOrDefault(File.Exists) ?? Path.Combine(baseDir, templateFile);
        }

        private static string? TryFindProjectRoot()
        {
            string[] seeds =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            foreach (string seed in seeds)
            {
                string current = seed;
                for (int i = 0; i < 8; i++)
                {
                    if (File.Exists(Path.Combine(current, "SmartSembakoAssistant.csproj")))
                    {
                        return current;
                    }

                    var parent = Directory.GetParent(current);
                    if (parent == null)
                    {
                        break;
                    }

                    current = parent.FullName;
                }
            }

            return null;
        }

        private static string? ChooseBestExistingConfig(IEnumerable<string> candidates)
        {
            return candidates
                .Where(File.Exists)
                .Select(path => new { Path = path, Score = ScoreConfig(path) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path.Length)
                .Select(x => x.Path)
                .FirstOrDefault();
        }

        private void AnalyzeConfigCandidates(string configPath)
        {
            if (Path.IsPathRooted(configPath))
            {
                DetectedConfigPaths = File.Exists(configPath)
                    ? new[] { configPath }
                    : Array.Empty<string>();
                DuplicateConfigWarning = null;
                return;
            }

            var existing = GetConfigCandidates(configPath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            DetectedConfigPaths = existing;
            if (existing.Count <= 1)
            {
                DuplicateConfigWarning = null;
                return;
            }

            var uniqueHashes = existing
                .Select(SafeReadFingerprint)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (uniqueHashes.Count > 1)
            {
                DuplicateConfigWarning = $"Terdeteksi lebih dari satu config.json dengan isi berbeda. Config aktif: {_configPath}";
            }
        }

        private static IEnumerable<string> GetConfigCandidates(string configPath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            string? projectRoot = TryFindProjectRoot();

            yield return RuntimePaths.ResolveWritablePath(configPath, configPath);

            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                yield return Path.Combine(projectRoot, configPath);
            }

            yield return Path.Combine(baseDir, configPath);
            yield return Path.Combine(currentDir, configPath);
        }

        private static string SafeReadFingerprint(string path)
        {
            try
            {
                return Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(path)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ScoreConfig(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                if (config == null)
                {
                    return 0;
                }

                int score = 0;

                if (!string.IsNullOrWhiteSpace(config.Groq?.ApiKey)) score += 2;
                if (!string.IsNullOrWhiteSpace(config.Groq?.FallbackApiKey)) score += 1;
                if (!string.IsNullOrWhiteSpace(config.Telegram?.BotToken)) score += 4;
                if (config.Telegram?.OwnerChatIds?.Any() == true) score += 1;
                if (!string.IsNullOrWhiteSpace(config.WhatsApp?.AccessToken)) score += 2;
                if (!string.IsNullOrWhiteSpace(config.WhatsApp?.PhoneNumberId)) score += 1;
                if (!string.IsNullOrWhiteSpace(config.Baileys?.BotPhoneNumber)) score += 2;
                if (config.Baileys?.OwnerNumbers?.Any() == true) score += 1;
                if (config.Setup?.SetupCompleted == true) score += 3;

                return score;
            }
            catch
            {
                return 0;
            }
        }

        private void LoadOcrMappingsIntoConfig(List<OcrProductMapping> legacyOcrMappings)
        {
            _config ??= new AppConfig();
            _config.OcrReceipt ??= new OcrReceiptSettings();

            List<OcrProductMapping> externalMappings = ReadOcrMappingsFromFile();
            if (externalMappings.Count > 0)
            {
                _config.OcrReceipt.ProductMappings = externalMappings;
                return;
            }

            _config.OcrReceipt.ProductMappings = CloneOcrMappings(legacyOcrMappings);
            if (legacyOcrMappings.Count > 0)
            {
                SaveOcrMappings(legacyOcrMappings);
            }
        }

        private List<OcrProductMapping> ReadOcrMappingsFromFile()
        {
            try
            {
                if (!File.Exists(_ocrMappingsPath))
                {
                    return new List<OcrProductMapping>();
                }

                string json = File.ReadAllText(_ocrMappingsPath);
                return CloneOcrMappings(JsonConvert.DeserializeObject<List<OcrProductMapping>>(json));
            }
            catch
            {
                return new List<OcrProductMapping>();
            }
        }

        private void SaveOcrMappings(IEnumerable<OcrProductMapping>? mappings)
        {
            List<OcrProductMapping> safeMappings = CloneOcrMappings(mappings);
            string? directory = Path.GetDirectoryName(_ocrMappingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonConvert.SerializeObject(safeMappings, Formatting.Indented);
            File.WriteAllText(_ocrMappingsPath, json);
        }

        private static List<OcrProductMapping> ExtractLegacyOcrMappings(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                return CloneOcrMappings(root["OcrReceipt"]?["ProductMappings"]?.ToObject<List<OcrProductMapping>>());
            }
            catch
            {
                return new List<OcrProductMapping>();
            }
        }

        private static List<OcrProductMapping> CloneOcrMappings(IEnumerable<OcrProductMapping>? mappings)
        {
            return mappings?
                .Where(mapping => mapping != null)
                .Select(mapping => new OcrProductMapping
                {
                    InvoiceName = mapping.InvoiceName,
                    DatabaseProductId = mapping.DatabaseProductId,
                    DatabaseProductName = mapping.DatabaseProductName
                })
                .ToList() ?? new List<OcrProductMapping>();
        }

        private static string ResolveSiblingPath(string baseFilePath, string relativePath)
        {
            string baseDirectory = Path.GetDirectoryName(baseFilePath) ?? Environment.CurrentDirectory;
            return Path.Combine(baseDirectory, relativePath);
        }
    }
}
