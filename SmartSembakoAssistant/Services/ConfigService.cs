using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class ConfigService
    {
        private readonly string _configPath;
        private readonly LoggingService? _loggingService;
        private AppConfig? _config;

        public AppConfig? Config => _config;

        public ConfigService(string configPath = "config.json", LoggingService? loggingService = null)
        {
            _configPath = configPath;
            _loggingService = loggingService;
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    // Copy dari template
                    string templatePath = "config.template.json";
                    if (File.Exists(templatePath))
                    {
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
                // Buat salinan config untuk di-encrypt (jangan modify original)
                var configCopy = new AppConfig
                {
                    Groq = _config?.Groq != null ? new GroqSettings
                    {
                        ApiKey = EncryptValue(_config.Groq.ApiKey),
                        Model = _config.Groq.Model,
                        FallbackApiKey = EncryptValue(_config.Groq.FallbackApiKey),
                        FallbackModel = _config.Groq.FallbackModel,
                        TimeoutSeconds = _config.Groq.TimeoutSeconds,
                        MaxTokens = _config.Groq.MaxTokens,
                        Temperature = _config.Groq.Temperature
                    } : null,
                    Telegram = _config?.Telegram != null ? new TelegramSettings
                    {
                        BotToken = EncryptValue(_config.Telegram.BotToken),
                        AllowedChatIds = _config.Telegram.AllowedChatIds?.ToList(),
                        RateLimitSeconds = _config.Telegram.RateLimitSeconds,
                        EnableVoiceNotes = _config.Telegram.EnableVoiceNotes
                    } : null,
                    PosDb = _config?.PosDb,
                    GoogleSheets = _config?.GoogleSheets,
                    Memory = _config?.Memory,
                    Notifications = _config?.Notifications
                };

                string json = JsonConvert.SerializeObject(configCopy, Formatting.Indented);
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
            catch (Exception ex)
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

        public void UpdateAppSettings(AppSettings settings)
        {
            if (_config != null)
            {
                _config.App = settings;
                SaveConfig();
            }
        }

        public bool IsConfigured()
        {
            return _config?.Groq?.ApiKey != null && 
                   _config?.Groq?.ApiKey != "YOUR_GROQ_API_KEY" &&
                   _config?.Telegram?.BotToken != null && 
                   _config?.Telegram?.BotToken != "YOUR_TELEGRAM_BOT_TOKEN";
        }
    }
}
