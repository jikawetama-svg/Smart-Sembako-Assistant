using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;
using SmartSembakoAssistant.Controls;

namespace SmartSembakoAssistant.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly PosDbService? _posDbService;
        private readonly LoggingService _loggingService;

        // Hide/unhide state
        private bool _groqKeyVisible = false;
        private bool _geminiKeyVisible = false;
        private bool _botTokenVisible = false;

        // Store actual values
        private string _actualGroqKey = "";
        private string _actualGeminiKey = "";
        private string _actualBotToken = "";

        public SettingsView(ConfigService configService, PosDbService? posDbService = null)
        {
            InitializeComponent();

            _configService = configService;
            _posDbService = posDbService;
            
            var dbService = new DatabaseService();
            _loggingService = new LoggingService(dbService);

            LoadSettings();
        }

        private void LoadSettings()
        {
            var config = _configService.Config;

            // Groq Settings
            if (config?.Groq != null)
            {
                _actualGroqKey = config.Groq.ApiKey == "YOUR_GROQ_API_KEY" ? "" : config.Groq.ApiKey;
                TxtGroqApiKey.Text = "••••••••••••";
                _groqKeyVisible = false;
                BtnShowGroqKey.Content = "👁️";
                
                // Set Groq Model
                string groqModel = config.Groq.Model ?? "llama-3.1-8b-instant";
                foreach (var item in CmbGroqModel.Items)
                {
                    if (item is ComboBoxItem cbItem && cbItem.Content?.ToString() == groqModel)
                    {
                        CmbGroqModel.SelectedItem = item;
                        break;
                    }
                }

                _actualGeminiKey = config.Groq.FallbackApiKey == "YOUR_GEMINI_API_KEY" ? "" : config.Groq.FallbackApiKey;
                TxtGeminiApiKey.Text = "••••••••••••";
                _geminiKeyVisible = false;
                BtnShowGeminiKey.Content = "👁️";
                
                // Set Gemini Model
                string geminiModel = config.Groq.FallbackModel ?? "gemini-3.1-flash-lite-preview";
                foreach (var item in CmbGeminiModel.Items)
                {
                    if (item is ComboBoxItem cbItem && cbItem.Content?.ToString() == geminiModel)
                    {
                        CmbGeminiModel.SelectedItem = item;
                        break;
                    }
                }
                
                TxtMaxTokens.Text = config.Groq.MaxTokens.ToString();
                TxtTemperature.Text = config.Groq.Temperature.ToString("F1");
            }

            // Telegram Settings
            if (config?.Telegram != null)
            {
                _actualBotToken = config.Telegram.BotToken == "YOUR_TELEGRAM_BOT_TOKEN" ? "" : config.Telegram.BotToken;
                TxtBotToken.Text = "••••••••••••";
                _botTokenVisible = false;
                BtnShowBotToken.Content = "👁️";
                TxtOwnerChatIds.Text = config.Telegram.OwnerChatIds != null
                    ? string.Join(", ", config.Telegram.OwnerChatIds)
                    : "";
            }

            // PosDb Settings
            if (config?.PosDb != null)
            {
                TxtPosDbPath.Text = config.PosDb.DatabasePath ?? "";
                ChkAutoDetect.IsChecked = config.PosDb.AutoDetect;

                if (config.PosDb.AutoDetect)
                {
                    string? autoPath = PosDbService.AutoDetectPosDbPath();
                    if (!string.IsNullOrEmpty(autoPath))
                    {
                        TxtPosDbPath.Text = autoPath;
                    }
                }
            }
        }

        // Show/Hide Groq API Key
        private void BtnShowGroqKey_Click(object sender, RoutedEventArgs e)
        {
            _groqKeyVisible = !_groqKeyVisible;
            if (_groqKeyVisible)
            {
                TxtGroqApiKey.Text = _actualGroqKey;
                BtnShowGroqKey.Content = "🙈";
            }
            else
            {
                TxtGroqApiKey.Text = "••••••••••••";
                BtnShowGroqKey.Content = "👁️";
            }
        }

        // Update actual Groq key when typing
        private void TxtGroqApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only update if visible and text is not dots (masked)
            if (_groqKeyVisible && TxtGroqApiKey.Text != "••••••••••••")
            {
                _actualGroqKey = TxtGroqApiKey.Text;
            }
        }

        // Show/Hide Gemini API Key
        private void BtnShowGeminiKey_Click(object sender, RoutedEventArgs e)
        {
            _geminiKeyVisible = !_geminiKeyVisible;
            if (_geminiKeyVisible)
            {
                TxtGeminiApiKey.Text = _actualGeminiKey;
                BtnShowGeminiKey.Content = "🙈";
            }
            else
            {
                TxtGeminiApiKey.Text = "••••••••••••";
                BtnShowGeminiKey.Content = "👁️";
            }
        }

        // Update actual Gemini key when typing
        private void TxtGeminiApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only update if visible (user is typing)
            if (_geminiKeyVisible)
            {
                _actualGeminiKey = TxtGeminiApiKey.Text;
            }
        }

        // Show/Hide Bot Token
        private void BtnShowBotToken_Click(object sender, RoutedEventArgs e)
        {
            _botTokenVisible = !_botTokenVisible;
            if (_botTokenVisible)
            {
                TxtBotToken.Text = _actualBotToken;
                BtnShowBotToken.Content = "🙈";
            }
            else
            {
                TxtBotToken.Text = "••••••••••••";
                BtnShowBotToken.Content = "👁️";
            }
        }

        // Update actual Bot Token when typing
        private void TxtBotToken_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only update if visible (user is typing)
            if (_botTokenVisible)
            {
                _actualBotToken = TxtBotToken.Text;
            }
        }

        // Fallback checkbox handler
        private void ChkEnableFallback_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkEnableFallback.IsChecked == true;
            TxtGeminiApiKey.IsEnabled = enabled;
            CmbGeminiModel.IsEnabled = enabled;
            BtnShowGeminiKey.IsEnabled = enabled;
            BtnTestGemini.IsEnabled = enabled;
        }

        // Auto-detect checkbox handler
        private void ChkAutoDetect_Changed(object sender, RoutedEventArgs e)
        {
            bool autoDetect = ChkAutoDetect.IsChecked == true;
            TxtPosDbPath.IsEnabled = !autoDetect;
            BtnBrowse.IsEnabled = !autoDetect;

            if (autoDetect)
            {
                string? autoPath = PosDbService.AutoDetectPosDbPath();
                if (!string.IsNullOrEmpty(autoPath))
                {
                    TxtPosDbPath.Text = autoPath;
                }
            }
        }

        // Browse button handler
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                Title = "Select pos.db file"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtPosDbPath.Text = dialog.FileName;
            }
        }

        // Test Groq API
        private async void BtnTestGroq_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnTestGroq.Content = "⏳ Testing...";
                BtnTestGroq.IsEnabled = false;

                var groqService = new GroqService(_configService, _loggingService);
                var (success, message) = await groqService.TestGroqConnectionAsync();

                if (success)
                {
                    ToastHelper.ShowSuccess("Groq Connected", message);
                }
                else
                {
                    ToastHelper.ShowError("Groq Test Failed", message);
                }
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Groq Test Failed", ex.Message);
            }
            finally
            {
                BtnTestGroq.Content = "🧪 Test";
                BtnTestGroq.IsEnabled = true;
            }
        }

        // Test Gemini API
        private async void BtnTestGemini_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnTestGemini.Content = "⏳ Testing...";
                BtnTestGemini.IsEnabled = false;

                var groqService = new GroqService(_configService, _loggingService);
                var (success, message) = await groqService.TestGeminiConnectionAsync();

                if (success)
                {
                    ToastHelper.ShowSuccess("Gemini Connected", message);
                }
                else
                {
                    ToastHelper.ShowError("Gemini Test Failed", message);
                }
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Gemini Test Failed", ex.Message);
            }
            finally
            {
                BtnTestGemini.Content = "🧪 Test";
                BtnTestGemini.IsEnabled = true;
            }
        }

        // Save settings
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = _configService.Config ?? new Models.AppConfig();

                // Update Groq Settings
                string groqModel = (CmbGroqModel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "llama-3.1-8b-instant";
                string geminiModel = (CmbGeminiModel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gemini-3.1-flash-lite-preview";

                // Use actual values if visible, otherwise use stored values
                // If text is dots (masked), use _actualGroqKey
                string groqKey = (_groqKeyVisible && TxtGroqApiKey.Text != "••••••••••••") 
                    ? TxtGroqApiKey.Text 
                    : _actualGroqKey;
                    
                string geminiKey = (_geminiKeyVisible && TxtGeminiApiKey.Text != "••••••••••••") 
                    ? TxtGeminiApiKey.Text 
                    : _actualGeminiKey;

                config.Groq = new Models.GroqSettings
                {
                    ApiKey = string.IsNullOrEmpty(groqKey) ? "YOUR_GROQ_API_KEY" : groqKey,
                    Model = groqModel,
                    FallbackApiKey = string.IsNullOrEmpty(geminiKey) ? "YOUR_GEMINI_API_KEY" : geminiKey,
                    FallbackModel = geminiModel,
                    MaxTokens = int.TryParse(TxtMaxTokens.Text, out int maxTokens) ? maxTokens : 1000,
                    Temperature = double.TryParse(TxtTemperature.Text, out double temp) ? temp : 0.7
                };

                // Update Telegram Settings
                // If text is dots (masked), use _actualBotToken
                string botToken = (_botTokenVisible && TxtBotToken.Text != "••••••••••••") 
                    ? TxtBotToken.Text 
                    : _actualBotToken;

                config.Telegram = new Models.TelegramSettings
                {
                    BotToken = string.IsNullOrEmpty(botToken) ? "YOUR_TELEGRAM_BOT_TOKEN" : botToken,
                    OwnerChatIds = string.IsNullOrEmpty(TxtOwnerChatIds.Text)
                        ? new List<long>()
                        : TxtOwnerChatIds.Text.Split(',')
                            .Select(s => long.TryParse(s.Trim(), out long id) ? id : 0)
                            .Where(id => id > 0)
                            .ToList(),
                    RateLimitSeconds = 5,
                    EnableVoiceNotes = false
                };

                // Update PosDb Settings
                config.PosDb = new Models.PosDbSettings
                {
                    DatabasePath = TxtPosDbPath.Text,
                    AutoDetect = ChkAutoDetect.IsChecked ?? false
                };

                // Update Notification Settings - simplified
                config.Notifications = new Models.NotificationSettings
                {
                    CheckIntervalMinutes = 5,
                    EnableDailySummary = false,
                    DailySummaryTime = "07:00",
                    StockThresholds = new List<Models.StockThreshold>
                    {
                        new Models.StockThreshold { Level = 10, Priority = "Low" },
                        new Models.StockThreshold { Level = 20, Priority = "Medium" }
                    }
                };

                // Save settings
                _configService.UpdateGroqSettings(config.Groq);
                _configService.UpdateTelegramSettings(config.Telegram);
                _configService.UpdatePosDbSettings(config.PosDb);
                _configService.UpdateNotificationSettings(config.Notifications);

                ToastHelper.ShowSuccess("Settings Saved", "Your configuration has been saved successfully.");
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Save Failed", ex.Message);
            }
        }

        // Test all connections
        private void BtnTestConnections_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("🧪 Test semua koneksi?\n\n• Groq AI\n• Gemini AI\n• Telegram Bot\n• Database",
                "Test Connections", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            string results = "🧪 HASIL TEST CONNECTIONS\n\n";

            // Test Groq
            var config = _configService.Config;
            if (config?.Groq?.ApiKey != null && config.Groq.ApiKey != "YOUR_GROQ_API_KEY")
            {
                results += "🧠 Groq AI: ✅ Configured\n";
            }
            else
            {
                results += "🧠 Groq AI: ❌ Not Configured\n";
            }

            // Test Gemini
            if (config?.Groq?.FallbackApiKey != null && config.Groq.FallbackApiKey != "YOUR_GEMINI_API_KEY")
            {
                results += "🔄 Gemini AI: ✅ Configured\n";
            }
            else
            {
                results += "🔄 Gemini AI: ❌ Not Configured\n";
            }

            // Test Telegram
            if (config?.Telegram?.BotToken != null && config.Telegram.BotToken != "YOUR_TELEGRAM_BOT_TOKEN")
            {
                results += "🤖 Telegram Bot: ✅ Configured\n";
            }
            else
            {
                results += "🤖 Telegram Bot: ❌ Not Configured\n";
            }

            // Test Database
            if (_posDbService != null)
            {
                results += "💾 Database: ✅ Connected\n";
            }
            else
            {
                results += "💾 Database: ❌ Not Connected\n";
            }

            ToastHelper.ShowInfo("Test Connections", results, Window.GetWindow(this));
        }
    }
}
