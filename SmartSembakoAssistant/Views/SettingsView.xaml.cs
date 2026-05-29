using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly PosDbService? _posDbService;
        private readonly LoggingService _loggingService;
        private readonly DatabaseService _databaseService;

        private bool _isLoading;
        private bool _isDirty;
        private bool _groqKeyVisible;
        private bool _geminiKeyVisible;
        private bool _botTokenVisible;
        private bool _whatsAppAccessTokenVisible;
        private bool _whatsAppAppSecretVisible;
        private bool _whatsAppVerifyTokenVisible;
        private readonly ObservableCollection<OcrProductMapping> _ocrProductMappings = new();
        private readonly ObservableCollection<OcrMappingRegistryRow> _ocrMappingRegistryRows = new();
        private readonly ObservableCollection<OcrReviewQueueItem> _ocrReviewQueueItems = new();
        private readonly ObservableCollection<UnitConversionMapping> _unitConversionMappings = new();
        private string? _selectedMappingProductId;
        private string? _selectedUnitConversionId;
        private string? _selectedParentConversionProductId;
        private string? _selectedChildConversionProductId;

        private sealed class OcrMappingRegistryRow
        {
            public string SupplierKey { get; set; } = "GLOBAL";
            public string InvoiceName { get; set; } = "";
            public string DatabaseProductId { get; set; } = "";
            public string DatabaseProductName { get; set; } = "";
            public string Source { get; set; } = "";
            public string TrustLevel { get; set; } = "";
            public DateTime? UpdatedAt { get; set; }
            public bool IsRuntimeAlias { get; set; }
            public string Origin => IsRuntimeAlias ? "Alias DB" : "JSON";
        }

        public SettingsView(ConfigService configService, PosDbService? posDbService = null)
        {
            InitializeComponent();
            _configService = configService;
            _posDbService = posDbService;
            _databaseService = new DatabaseService();
            _loggingService = new LoggingService(_databaseService);
            DgProductMappings.ItemsSource = _ocrMappingRegistryRows;
            DgOcrReviewQueue.ItemsSource = _ocrReviewQueueItems;
            DgUnitConversions.ItemsSource = _unitConversionMappings;

            WireDirtyTracking();
            LoadSettings();
            Loaded += async (_, _) =>
            {
                await RefreshOcrMappingRegistryAsync();
                await RefreshOcrReviewQueueAsync();
                await RefreshUnitConversionsAsync();
            };
        }

        private void WireDirtyTracking()
        {
            AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnyInputChanged));
            AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
            AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(AnyToggleChanged));
            AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        }

        private void LoadSettings()
        {
            _isLoading = true;
            try
            {
                var config = _configService.Config ?? new AppConfig();
                config.WhatsApp ??= new WhatsAppSettings();
                config.Baileys ??= new BaileysSettings();
                config.Tunnel ??= new TunnelSettings();
                config.Automation ??= new AutomationSettings();
                config.PosDb ??= new PosDbSettings();
                config.Groq ??= new GroqSettings();
                config.Telegram ??= new TelegramSettings();
                config.GoogleSheets ??= new GoogleSheetsSettings();
                config.OcrReceipt ??= new OcrReceiptSettings();

                TxtGroqApiKey.Text = NormalizeSecret(config.Groq.ApiKey, "YOUR_GROQ_API_KEY");
                TxtGeminiApiKey.Text = NormalizeSecret(config.Groq.FallbackApiKey, "YOUR_GEMINI_API_KEY");
                SelectComboItem(CmbGroqModel, config.Groq.Model ?? "llama-3.3-70b-versatile");
                SelectComboItem(CmbGeminiModel, config.Groq.FallbackModel ?? "gemini-2.5-flash");
                SelectComboItem(CmbGeminiVisionModel, config.Groq.VisionModel ?? config.Groq.FallbackModel ?? "gemini-2.5-flash");
                TxtMaxTokens.Text = config.Groq.MaxTokens.ToString();
                TxtTemperature.Text = config.Groq.Temperature.ToString("F1");
                ChkEnableFallback.IsChecked = !string.IsNullOrWhiteSpace(TxtGeminiApiKey.Text);

                TxtBotToken.Text = NormalizeSecret(config.Telegram.BotToken, "YOUR_TELEGRAM_BOT_TOKEN");
                TxtOwnerChatIds.Text = string.Join(", ", config.Telegram.OwnerChatIds ?? new List<long>());
                TxtKasirChatIds.Text = string.Join(", ", config.Telegram.KasirChatIds ?? new List<long>());

                ChkWhatsAppEnabled.IsChecked = config.WhatsApp.Enabled;
                SelectComboItem(CmbWhatsAppMode, WhatsAppModes.Normalize(config.WhatsApp.Mode));
                TxtWhatsAppAccessToken.Text = NormalizeSecret(config.WhatsApp.AccessToken, "");
                TxtWhatsAppPhoneNumberId.Text = config.WhatsApp.PhoneNumberId ?? "";
                TxtWhatsAppAppSecret.Text = NormalizeSecret(config.WhatsApp.AppSecret, "");
                TxtWhatsAppVerifyToken.Text = NormalizeSecret(config.WhatsApp.VerifyToken, "");
                TxtWhatsAppOwnerNumbers.Text = string.Join(", ", config.WhatsApp.OwnerNumbers ?? new List<string>());
                TxtWhatsAppKasirNumbers.Text = string.Join(", ", config.WhatsApp.KasirNumbers ?? new List<string>());
                TxtWhatsAppLocalPort.Text = config.WhatsApp.LocalWebhookPort.ToString();
                TxtWhatsAppPublicUrl.Text = config.WhatsApp.PublicWebhookUrl ?? "";
                TxtWhatsAppMaxRetries.Text = config.WhatsApp.OutboundMaxRetries.ToString();
                TxtWhatsAppRetryDelay.Text = config.WhatsApp.InitialRetryDelaySeconds.ToString();
                ChkWhatsAppTemplateMessages.IsChecked = config.WhatsApp.EnableTemplateMessages;
                TxtWhatsAppTemplateLanguage.Text = config.WhatsApp.DefaultTemplateLanguageCode ?? "id";
                TxtWhatsAppTemplateMappings.Text = FormatWhatsAppTemplateMappings(config.WhatsApp.TemplateMappings);

                ChkBaileysEnabled.IsChecked = config.Baileys.Enabled;
                ChkBaileysAutoStart.IsChecked = config.Baileys.AutoStart;
                TxtBaileysBotPhoneNumber.Text = config.Baileys.BotPhoneNumber ?? "";
                TxtBaileysNodeBinaryPath.Text = string.IsNullOrWhiteSpace(config.Baileys.NodeBinaryPath)
                    ? "runtimes\\node\\node.exe"
                    : config.Baileys.NodeBinaryPath;
                TxtBaileysSidecarEntryPath.Text = config.Baileys.SidecarEntryPath ?? "";
                TxtBaileysWorkingDirectory.Text = config.Baileys.WorkingDirectory ?? "";
                TxtBaileysSessionPath.Text = config.Baileys.SessionPath ?? "";
                TxtBaileysLocalApiPort.Text = config.Baileys.LocalApiPort.ToString();
                TxtBaileysOwnerNumbers.Text = string.Join(", ", config.Baileys.OwnerNumbers ?? new List<string>());
                TxtBaileysKasirNumbers.Text = string.Join(", ", config.Baileys.KasirNumbers ?? new List<string>());

                ChkTunnelEnabled.IsChecked = config.Tunnel.Enabled;
                SelectComboItem(CmbTunnelProvider, config.Tunnel.Provider ?? "cloudflared");
                TxtTunnelBinaryPath.Text = string.IsNullOrWhiteSpace(config.Tunnel.BinaryPath)
                    ? "runtimes\\cloudflared\\cloudflared.exe"
                    : config.Tunnel.BinaryPath;
                TxtTunnelArgsTemplate.Text = config.Tunnel.ArgsTemplate ?? "";
                TxtTunnelPublicUrl.Text = config.Tunnel.PublicUrl ?? "";

                ChkEnableTemplates.IsChecked = config.Automation.EnableTemplates;
                ChkEnableLowStockAlerts.IsChecked = config.Automation.EnableLowStockAlerts;
                TxtLowStockAlertTime.Text = config.Automation.LowStockAlertTime ?? "07:00";
                ChkEnableTelegramLowStockAlerts.IsChecked = config.Automation.EnableTelegramLowStockAlerts;
                ChkEnableWhatsAppCloudLowStockAlerts.IsChecked = config.Automation.EnableWhatsAppCloudLowStockAlerts;
                ChkEnableBaileysLowStockAlerts.IsChecked = config.Automation.EnableBaileysLowStockAlerts;
                ChkEnableDailySummary.IsChecked = config.Automation.EnableDailySummary;
                TxtDailySummaryTime.Text = config.Automation.DailySummaryTime ?? "21:15";
                ChkEnableTelegramDailySummaryAlerts.IsChecked = config.Automation.EnableTelegramDailySummaryAlerts;
                ChkEnableWhatsAppCloudDailySummaryAlerts.IsChecked = config.Automation.EnableWhatsAppCloudDailySummaryAlerts;
                ChkEnableBaileysDailySummaryAlerts.IsChecked = config.Automation.EnableBaileysDailySummaryAlerts;
                ChkEnableWeeklyReport.IsChecked = config.Automation.EnableWeeklyReport;
                TxtWeeklyReportTime.Text = config.Automation.WeeklyReportTime ?? "07:00";
                ChkEnableTelegramWeeklyReportAlerts.IsChecked = config.Automation.EnableTelegramWeeklyReportAlerts;
                ChkEnableWhatsAppCloudWeeklyReportAlerts.IsChecked = config.Automation.EnableWhatsAppCloudWeeklyReportAlerts;
                ChkEnableBaileysWeeklyReportAlerts.IsChecked = config.Automation.EnableBaileysWeeklyReportAlerts;
                ChkEnableAIReportNarrative.IsChecked = config.Automation.EnableAIReportNarrative;
                ChkDualStockEnabled.IsChecked = config.Automation.EnableDualStockSync;
                ChkDualStockRealtimeWatcherEnabled.IsChecked = config.Automation.EnableDualStockRealtimeWatcher;
                ChkEnableTelegramDualStockAlerts.IsChecked = config.Automation.EnableTelegramDualStockAlerts;
                ChkEnableWhatsAppCloudDualStockAlerts.IsChecked = config.Automation.EnableWhatsAppCloudDualStockAlerts;
                ChkEnableBaileysDualStockAlerts.IsChecked = config.Automation.EnableBaileysDualStockAlerts;
                TxtDualStockSyncInterval.Text = Math.Clamp(config.Automation.DualStockSyncIntervalSeconds, 5, 3600).ToString();
                TxtDualStockDailySyncTime.Text = config.Automation.DualStockDailySyncTime ?? "21:00";
                TxtAutomationTemplates.Text = string.Join(Environment.NewLine,
                    (config.Automation.Templates ?? new List<AutomationTemplate>())
                        .Select(t => $"{t.Key} - {t.Name}"));

                TxtPosDbPath.Text = config.PosDb.DatabasePath ?? "";
                ChkAutoDetect.IsChecked = config.PosDb.AutoDetect;
                ChkOcrEnabled.IsChecked = config.OcrReceipt.Enabled;
                TxtOcrTriggerCaption.Text = config.OcrReceipt.TriggerCaption ?? "/struk";
                TxtTessdataPath.Text = config.OcrReceipt.TessdataPath ?? "tessdata";
                LoadOcrMappings(config.OcrReceipt.ProductMappings);

                ChkSheetsEnabled.IsChecked = config.GoogleSheets.Enabled;
                TxtSheetsCredentialPath.Text = config.GoogleSheets.CredentialsJsonPath ?? "";
                TxtSheetsSpreadsheetId.Text = config.GoogleSheets.SpreadsheetId ?? "";
                TxtSheetsPurchaseTabName.Text = config.GoogleSheets.PurchaseSheetName ?? "Pembelian";
                ClearMappingInputs();
                TxtOcrValidation.ToolTip = _configService.OcrMappingsPath;

                ApplySecretVisibility(TxtGroqApiKey, OverlayGroqApiKey, TxtGroqApiKeyMasked, _groqKeyVisible, BtnShowGroqKey);
                ApplySecretVisibility(TxtGeminiApiKey, OverlayGeminiApiKey, TxtGeminiApiKeyMasked, _geminiKeyVisible, BtnShowGeminiKey);
                ApplySecretVisibility(TxtBotToken, OverlayBotToken, TxtBotTokenMasked, _botTokenVisible, BtnShowBotToken);
                ApplySecretVisibility(TxtWhatsAppAccessToken, OverlayWhatsAppAccessToken, TxtWhatsAppAccessTokenMasked, _whatsAppAccessTokenVisible, BtnShowWhatsAppAccessToken);
                ApplySecretVisibility(TxtWhatsAppAppSecret, OverlayWhatsAppAppSecret, TxtWhatsAppAppSecretMasked, _whatsAppAppSecretVisible, BtnShowWhatsAppAppSecret);
                ApplySecretVisibility(TxtWhatsAppVerifyToken, OverlayWhatsAppVerifyToken, TxtWhatsAppVerifyTokenMasked, _whatsAppVerifyTokenVisible, BtnShowWhatsAppVerifyToken);

                UpdateEnabledStates();
                ValidateForm();
                _isDirty = false;
                UpdateDraftStatus();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private static string NormalizeSecret(string? value, string placeholder)
        {
            if (string.IsNullOrWhiteSpace(value) || value == placeholder)
            {
                return "";
            }

            return value;
        }

        private static void SelectComboItem(ComboBox comboBox, string value)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private static string GetComboValue(ComboBox comboBox, string fallback)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
        }

        private void AnyInputChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            MarkDirty();
        }

        private void AnySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            MarkDirty();
        }

        private void AnyToggleChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            MarkDirty();
            UpdateEnabledStates();
        }

        private void MarkDirty()
        {
            _isDirty = true;
            UpdateEnabledStates();
            ValidateForm();
            UpdateDraftStatus();
        }

        private void UpdateDraftStatus()
        {
            TxtDraftStatus.Text = _isDirty
                ? "Draft berubah, belum disimpan. Tombol test memakai nilai draft saat ini tanpa menulis ke config."
                : "Semua perubahan sudah tersimpan.";
            TxtDraftStatus.Foreground = _isDirty
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B45309"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        }

        private void UpdateEnabledStates()
        {
            bool fallbackEnabled = ChkEnableFallback.IsChecked == true;
            TxtGeminiApiKey.IsEnabled = fallbackEnabled;
            CmbGeminiModel.IsEnabled = fallbackEnabled;
            CmbGeminiVisionModel.IsEnabled = fallbackEnabled;
            BtnShowGeminiKey.IsEnabled = fallbackEnabled;
            BtnTestGemini.IsEnabled = fallbackEnabled;

            bool waEnabled = ChkWhatsAppEnabled.IsChecked == true;
            string waMode = GetComboValue(CmbWhatsAppMode, WhatsAppModes.CloudApi);
            bool cloudEnabled = waEnabled && WhatsAppModes.UsesCloudApi(waMode);
            bool baileysEnabled = ChkBaileysEnabled.IsChecked == true && WhatsAppModes.UsesBaileys(waMode);

            CmbWhatsAppMode.IsEnabled = waEnabled;
            TxtWhatsAppAccessToken.IsEnabled = cloudEnabled && _whatsAppAccessTokenVisible;
            BtnShowWhatsAppAccessToken.IsEnabled = cloudEnabled;
            TxtWhatsAppPhoneNumberId.IsEnabled = cloudEnabled;
            TxtWhatsAppAppSecret.IsEnabled = cloudEnabled && _whatsAppAppSecretVisible;
            BtnShowWhatsAppAppSecret.IsEnabled = cloudEnabled;
            TxtWhatsAppVerifyToken.IsEnabled = cloudEnabled && _whatsAppVerifyTokenVisible;
            BtnShowWhatsAppVerifyToken.IsEnabled = cloudEnabled;
            TxtWhatsAppOwnerNumbers.IsEnabled = cloudEnabled;
            TxtWhatsAppKasirNumbers.IsEnabled = cloudEnabled;
            TxtWhatsAppLocalPort.IsEnabled = waEnabled;
            TxtWhatsAppPublicUrl.IsEnabled = cloudEnabled;
            TxtWhatsAppMaxRetries.IsEnabled = waEnabled;
            TxtWhatsAppRetryDelay.IsEnabled = waEnabled;
            ChkWhatsAppTemplateMessages.IsEnabled = cloudEnabled;
            TxtWhatsAppTemplateLanguage.IsEnabled = cloudEnabled && ChkWhatsAppTemplateMessages.IsChecked == true;
            TxtWhatsAppTemplateMappings.IsEnabled = cloudEnabled && ChkWhatsAppTemplateMessages.IsChecked == true;
            BtnTestWhatsAppMeta.IsEnabled = cloudEnabled;
            BtnTestWhatsAppWebhook.IsEnabled = cloudEnabled;
            BtnTestWhatsAppSend.IsEnabled = cloudEnabled;
            BtnTestWhatsAppTemplate.IsEnabled = cloudEnabled && ChkWhatsAppTemplateMessages.IsChecked == true;

            TxtBaileysNodeBinaryPath.IsEnabled = baileysEnabled;
            TxtBaileysSidecarEntryPath.IsEnabled = baileysEnabled;
            TxtBaileysWorkingDirectory.IsEnabled = baileysEnabled;
            TxtBaileysSessionPath.IsEnabled = baileysEnabled;
            TxtBaileysLocalApiPort.IsEnabled = baileysEnabled;
            TxtBaileysBotPhoneNumber.IsEnabled = baileysEnabled;
            TxtBaileysOwnerNumbers.IsEnabled = baileysEnabled;
            TxtBaileysKasirNumbers.IsEnabled = baileysEnabled;
            ChkBaileysAutoStart.IsEnabled = baileysEnabled;
            BtnTestBaileys.IsEnabled = baileysEnabled;
            BtnStartBaileysPairing.IsEnabled = baileysEnabled;

            bool tunnelEnabled = ChkTunnelEnabled.IsChecked == true;
            CmbTunnelProvider.IsEnabled = tunnelEnabled;
            TxtTunnelBinaryPath.IsEnabled = tunnelEnabled;
            TxtTunnelArgsTemplate.IsEnabled = tunnelEnabled;
            TxtTunnelPublicUrl.IsEnabled = tunnelEnabled || !string.IsNullOrWhiteSpace(TxtTunnelPublicUrl.Text);
            BtnTestTunnel.IsEnabled = tunnelEnabled || !string.IsNullOrWhiteSpace(TxtTunnelPublicUrl.Text);

            bool telegramAlertReady = !string.IsNullOrWhiteSpace(TxtBotToken.Text) && ParseLongList(TxtOwnerChatIds.Text).Any();
            bool lowStockEnabled = ChkEnableLowStockAlerts.IsChecked == true;
            bool waCloudAlertReady = cloudEnabled && ParseWhatsAppNumbers(TxtWhatsAppOwnerNumbers.Text).Any();
            bool baileysAlertReady = baileysEnabled && ParseWhatsAppNumbers(TxtBaileysOwnerNumbers.Text).Any();

            TxtLowStockAlertTime.IsEnabled = lowStockEnabled;
            ChkEnableTelegramLowStockAlerts.IsEnabled = lowStockEnabled && telegramAlertReady;
            ChkEnableWhatsAppCloudLowStockAlerts.IsEnabled = lowStockEnabled && waCloudAlertReady;
            ChkEnableBaileysLowStockAlerts.IsEnabled = lowStockEnabled && baileysAlertReady;

            bool ocrEnabled = ChkOcrEnabled.IsChecked == true;
            TxtOcrTriggerCaption.IsEnabled = ocrEnabled;
            TxtTessdataPath.IsEnabled = ocrEnabled;
            BtnBrowseTessdata.IsEnabled = ocrEnabled;
            TxtMappingInvoiceName.IsEnabled = ocrEnabled;
            TxtMappingDbName.IsEnabled = ocrEnabled;
            CmbMappingSupplier.IsEnabled = ocrEnabled;
            CmbMappingStatus.IsEnabled = ocrEnabled;
            BtnSearchDbProduct.IsEnabled = ocrEnabled && _posDbService != null;
            BtnAddMapping.IsEnabled = ocrEnabled;
            BtnRefreshMappings.IsEnabled = ocrEnabled;
            DgProductMappings.IsEnabled = ocrEnabled;
            TxtConversionParentName.IsEnabled = ocrEnabled;
            TxtConversionChildName.IsEnabled = ocrEnabled;
            TxtConversionRate.IsEnabled = ocrEnabled;
            BtnSearchParentConversionProduct.IsEnabled = ocrEnabled && _posDbService != null;
            BtnSearchChildConversionProduct.IsEnabled = ocrEnabled && _posDbService != null;
            BtnAddUnitConversion.IsEnabled = ocrEnabled;
            BtnRefreshUnitConversions.IsEnabled = ocrEnabled;
            DgUnitConversions.IsEnabled = ocrEnabled;

            bool sheetsEnabled = ChkSheetsEnabled.IsChecked == true;
            TxtSheetsCredentialPath.IsEnabled = sheetsEnabled;
            BtnBrowseSheetsCredential.IsEnabled = sheetsEnabled;
            TxtSheetsSpreadsheetId.IsEnabled = sheetsEnabled;
            TxtSheetsPurchaseTabName.IsEnabled = sheetsEnabled;
            BtnPrepareSheets.IsEnabled = sheetsEnabled;
            BtnSyncSheetsDaily.IsEnabled = sheetsEnabled && _posDbService != null;
            BtnTestSheets.IsEnabled = sheetsEnabled;

            bool autoDetect = ChkAutoDetect.IsChecked == true;
            TxtPosDbPath.IsEnabled = !autoDetect;
            BtnBrowse.IsEnabled = !autoDetect;

            if (autoDetect)
            {
                string? path = PosDbService.AutoDetectPosDbPath();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    TxtPosDbPath.Text = path;
                }
            }

            ApplySecretVisibility(TxtGroqApiKey, OverlayGroqApiKey, TxtGroqApiKeyMasked, _groqKeyVisible, BtnShowGroqKey);
            ApplySecretVisibility(TxtGeminiApiKey, OverlayGeminiApiKey, TxtGeminiApiKeyMasked, _geminiKeyVisible, BtnShowGeminiKey);
            ApplySecretVisibility(TxtBotToken, OverlayBotToken, TxtBotTokenMasked, _botTokenVisible, BtnShowBotToken);
            ApplySecretVisibility(TxtWhatsAppAccessToken, OverlayWhatsAppAccessToken, TxtWhatsAppAccessTokenMasked, _whatsAppAccessTokenVisible, BtnShowWhatsAppAccessToken);
            ApplySecretVisibility(TxtWhatsAppAppSecret, OverlayWhatsAppAppSecret, TxtWhatsAppAppSecretMasked, _whatsAppAppSecretVisible, BtnShowWhatsAppAppSecret);
            ApplySecretVisibility(TxtWhatsAppVerifyToken, OverlayWhatsAppVerifyToken, TxtWhatsAppVerifyTokenMasked, _whatsAppVerifyTokenVisible, BtnShowWhatsAppVerifyToken);
        }

        private static void ApplySecretVisibility(TextBox textBox, Border overlay, TextBlock overlayText, bool isVisible, Button button)
        {
            bool hasValue = !string.IsNullOrWhiteSpace(textBox.Text);
            bool shouldMask = !isVisible && hasValue;
            overlay.Visibility = shouldMask ? Visibility.Visible : Visibility.Collapsed;
            overlayText.Text = hasValue ? "••••••••••••••••" : "";
            textBox.IsReadOnly = shouldMask;
            textBox.Foreground = shouldMask ? Brushes.Transparent : Brushes.Black;
            textBox.CaretBrush = shouldMask ? Brushes.Transparent : Brushes.Black;
            button.Content = isVisible ? "Hide" : "Show";
        }

        private void ToggleSecret(ref bool isVisible, TextBox textBox, Border overlay, TextBlock overlayText, Button button)
        {
            isVisible = !isVisible;
            ApplySecretVisibility(textBox, overlay, overlayText, isVisible, button);
        }

        private static List<long> ParseLongList(string? text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? new List<long>()
                : text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => long.TryParse(x.Trim(), out var id) ? id : 0)
                    .Where(x => x > 0)
                    .ToList();
        }

        private static List<string> ParseStringList(string? text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
        }

        private static List<string> ParseWhatsAppNumbers(string? text)
        {
            return ParseStringList(text)
                .Select(AutomationEngine.NormalizeWhatsAppNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string FormatWhatsAppTemplateMappings(IEnumerable<WhatsAppTemplateMapping>? mappings)
        {
            var safeMappings = mappings?.Where(mapping => !string.IsNullOrWhiteSpace(mapping.Key)).ToList()
                               ?? DefaultWhatsAppTemplateMappings();
            return string.Join(Environment.NewLine, safeMappings.Select(mapping =>
            {
                string language = string.IsNullOrWhiteSpace(mapping.LanguageCode) ? "id" : mapping.LanguageCode!;
                int parameterCount = Math.Max(0, mapping.BodyParameterCount);
                return $"{mapping.Key}={mapping.TemplateName}|{language}|{parameterCount}";
            }));
        }

        private static List<WhatsAppTemplateMapping> ParseWhatsAppTemplateMappings(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return DefaultWhatsAppTemplateMappings();
            }

            var mappings = new List<WhatsAppTemplateMapping>();
            foreach (string rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] keyAndValue = line.Split('=', 2);
                if (keyAndValue.Length != 2 || string.IsNullOrWhiteSpace(keyAndValue[0]))
                {
                    continue;
                }

                string[] parts = keyAndValue[1].Split('|', StringSplitOptions.TrimEntries);
                string templateName = parts.ElementAtOrDefault(0) ?? string.Empty;
                string? language = parts.ElementAtOrDefault(1);
                int bodyParameterCount = int.TryParse(parts.ElementAtOrDefault(2), out int parsedCount)
                    ? Math.Max(0, parsedCount)
                    : 1;

                mappings.Add(new WhatsAppTemplateMapping
                {
                    Key = keyAndValue[0].Trim(),
                    TemplateName = templateName.Trim(),
                    LanguageCode = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                    BodyParameterCount = bodyParameterCount
                });
            }

            return mappings.Any() ? mappings : DefaultWhatsAppTemplateMappings();
        }

        private static List<WhatsAppTemplateMapping> DefaultWhatsAppTemplateMappings()
        {
            return new List<WhatsAppTemplateMapping>
            {
                new() { Key = "StockAlert", TemplateName = "ssa_low_stock_alert", LanguageCode = "id", BodyParameterCount = 1 },
                new() { Key = "Schedule", TemplateName = "ssa_daily_summary", LanguageCode = "id", BodyParameterCount = 1 },
                new() { Key = "ReceivableAlert", TemplateName = "ssa_receivable_alert", LanguageCode = "id", BodyParameterCount = 1 },
                new() { Key = "ExpiryAlert", TemplateName = "ssa_expiry_alert", LanguageCode = "id", BodyParameterCount = 1 },
                new() { Key = "AnomalyAlert", TemplateName = "ssa_anomaly_alert", LanguageCode = "id", BodyParameterCount = 1 },
                new() { Key = "Test", TemplateName = "ssa_test_message", LanguageCode = "id", BodyParameterCount = 1 }
            };
        }

        private AppConfig BuildDraftConfig()
        {
            var current = _configService.CloneConfig();
            current.Groq ??= new GroqSettings();
            current.Telegram ??= new TelegramSettings();
            current.WhatsApp ??= new WhatsAppSettings();
            current.Baileys ??= new BaileysSettings();
            current.Tunnel ??= new TunnelSettings();
            current.Automation ??= new AutomationSettings();
            current.PosDb ??= new PosDbSettings();
            current.GoogleSheets ??= new GoogleSheetsSettings();
            current.OcrReceipt ??= new OcrReceiptSettings();
            current.Notifications ??= new NotificationSettings();
            current.App ??= new AppSettings();

            current.Groq.ApiKey = string.IsNullOrWhiteSpace(TxtGroqApiKey.Text) ? "YOUR_GROQ_API_KEY" : TxtGroqApiKey.Text.Trim();
            current.Groq.Model = GetComboValue(CmbGroqModel, "llama-3.3-70b-versatile");
            current.Groq.FallbackApiKey = ChkEnableFallback.IsChecked == true ? TxtGeminiApiKey.Text.Trim() : "";
            current.Groq.FallbackModel = GetComboValue(CmbGeminiModel, "gemini-2.5-flash");
            current.Groq.VisionModel = GetComboValue(CmbGeminiVisionModel, current.Groq.FallbackModel ?? "gemini-2.5-flash");
            current.Groq.MaxTokens = int.TryParse(TxtMaxTokens.Text, out var maxTokens) ? maxTokens : 500;
            current.Groq.Temperature = double.TryParse(TxtTemperature.Text, out var temperature) ? temperature : 0.7;

            current.Telegram.BotToken = string.IsNullOrWhiteSpace(TxtBotToken.Text) ? "YOUR_TELEGRAM_BOT_TOKEN" : TxtBotToken.Text.Trim();
            current.Telegram.OwnerChatIds = ParseLongList(TxtOwnerChatIds.Text);
            current.Telegram.KasirChatIds = ParseLongList(TxtKasirChatIds.Text);
            current.Telegram.AllowedChatIds = current.Telegram.OwnerChatIds
                .Concat(current.Telegram.KasirChatIds ?? new List<long>())
                .Distinct()
                .ToList();

            current.WhatsApp.Enabled = ChkWhatsAppEnabled.IsChecked == true;
            current.WhatsApp.Mode = WhatsAppModes.Normalize(GetComboValue(CmbWhatsAppMode, WhatsAppModes.CloudApi));
            current.WhatsApp.AccessToken = TxtWhatsAppAccessToken.Text.Trim();
            current.WhatsApp.PhoneNumberId = TxtWhatsAppPhoneNumberId.Text.Trim();
            current.WhatsApp.AppSecret = TxtWhatsAppAppSecret.Text.Trim();
            current.WhatsApp.VerifyToken = TxtWhatsAppVerifyToken.Text.Trim();
            current.WhatsApp.GraphApiVersion = "v22.0";
            current.WhatsApp.LocalWebhookPort = int.TryParse(TxtWhatsAppLocalPort.Text, out var waPort) ? waPort : 8090;
            current.WhatsApp.PublicWebhookUrl = TxtWhatsAppPublicUrl.Text.Trim();
            current.WhatsApp.EnableTemplateMessages = ChkWhatsAppTemplateMessages.IsChecked == true;
            current.WhatsApp.DefaultTemplateLanguageCode = string.IsNullOrWhiteSpace(TxtWhatsAppTemplateLanguage.Text)
                ? "id"
                : TxtWhatsAppTemplateLanguage.Text.Trim();
            current.WhatsApp.TemplateMappings = ParseWhatsAppTemplateMappings(TxtWhatsAppTemplateMappings.Text);
            current.WhatsApp.OutboundMaxRetries = int.TryParse(TxtWhatsAppMaxRetries.Text, out var waRetries) ? Math.Max(1, waRetries) : 5;
            current.WhatsApp.InitialRetryDelaySeconds = int.TryParse(TxtWhatsAppRetryDelay.Text, out var waDelay) ? Math.Max(1, waDelay) : 15;
            current.WhatsApp.OwnerNumbers = ParseWhatsAppNumbers(TxtWhatsAppOwnerNumbers.Text);
            current.WhatsApp.KasirNumbers = ParseWhatsAppNumbers(TxtWhatsAppKasirNumbers.Text);

            current.Baileys.Enabled = ChkBaileysEnabled.IsChecked == true;
            current.Baileys.BotPhoneNumber = TxtBaileysBotPhoneNumber.Text.Trim();
            current.Baileys.NodeBinaryPath = string.IsNullOrWhiteSpace(TxtBaileysNodeBinaryPath.Text)
                ? "runtimes\\node\\node.exe"
                : TxtBaileysNodeBinaryPath.Text.Trim();
            current.Baileys.SidecarEntryPath = TxtBaileysSidecarEntryPath.Text.Trim();
            current.Baileys.WorkingDirectory = TxtBaileysWorkingDirectory.Text.Trim();
            current.Baileys.SessionPath = TxtBaileysSessionPath.Text.Trim();
            current.Baileys.LocalApiPort = int.TryParse(TxtBaileysLocalApiPort.Text, out var baileysPort) ? baileysPort : 8091;
            current.Baileys.AutoStart = ChkBaileysAutoStart.IsChecked == true;
            current.Baileys.OwnerNumbers = ParseWhatsAppNumbers(TxtBaileysOwnerNumbers.Text);
            current.Baileys.KasirNumbers = ParseWhatsAppNumbers(TxtBaileysKasirNumbers.Text);

            current.Tunnel.Enabled = ChkTunnelEnabled.IsChecked == true;
            current.Tunnel.Provider = GetComboValue(CmbTunnelProvider, "cloudflared");
            current.Tunnel.BinaryPath = string.IsNullOrWhiteSpace(TxtTunnelBinaryPath.Text)
                ? "runtimes\\cloudflared\\cloudflared.exe"
                : TxtTunnelBinaryPath.Text.Trim();
            current.Tunnel.ArgsTemplate = TxtTunnelArgsTemplate.Text.Trim();
            current.Tunnel.PublicUrl = TxtTunnelPublicUrl.Text.Trim();

            current.Automation.EnableTemplates = ChkEnableTemplates.IsChecked == true;
            current.Automation.EnableLowStockAlerts = ChkEnableLowStockAlerts.IsChecked == true;
            current.Automation.LowStockAlertTime = TxtLowStockAlertTime.Text.Trim();
            current.Automation.EnableTelegramLowStockAlerts = ChkEnableTelegramLowStockAlerts.IsChecked == true;
            current.Automation.EnableWhatsAppCloudLowStockAlerts = ChkEnableWhatsAppCloudLowStockAlerts.IsChecked == true;
            current.Automation.EnableBaileysLowStockAlerts = ChkEnableBaileysLowStockAlerts.IsChecked == true;
            current.Automation.EnableDailySummary = ChkEnableDailySummary.IsChecked == true;
            current.Automation.DailySummaryTime = TxtDailySummaryTime.Text.Trim();
            current.Automation.EnableTelegramDailySummaryAlerts = ChkEnableTelegramDailySummaryAlerts.IsChecked == true;
            current.Automation.EnableWhatsAppCloudDailySummaryAlerts = ChkEnableWhatsAppCloudDailySummaryAlerts.IsChecked == true;
            current.Automation.EnableBaileysDailySummaryAlerts = ChkEnableBaileysDailySummaryAlerts.IsChecked == true;
            current.Automation.EnableWeeklyReport = ChkEnableWeeklyReport.IsChecked == true;
            current.Automation.WeeklyReportTime = TxtWeeklyReportTime.Text.Trim();
            current.Automation.EnableTelegramWeeklyReportAlerts = ChkEnableTelegramWeeklyReportAlerts.IsChecked == true;
            current.Automation.EnableWhatsAppCloudWeeklyReportAlerts = ChkEnableWhatsAppCloudWeeklyReportAlerts.IsChecked == true;
            current.Automation.EnableBaileysWeeklyReportAlerts = ChkEnableBaileysWeeklyReportAlerts.IsChecked == true;
            current.Automation.EnableAIReportNarrative = ChkEnableAIReportNarrative.IsChecked == true;
            current.Automation.EnableDualStockSync = ChkDualStockEnabled.IsChecked == true;
            current.Automation.EnableDualStockRealtimeWatcher = ChkDualStockRealtimeWatcherEnabled.IsChecked == true;
            current.Automation.EnableTelegramDualStockAlerts = ChkEnableTelegramDualStockAlerts.IsChecked == true;
            current.Automation.EnableWhatsAppCloudDualStockAlerts = ChkEnableWhatsAppCloudDualStockAlerts.IsChecked == true;
            current.Automation.EnableBaileysDualStockAlerts = ChkEnableBaileysDualStockAlerts.IsChecked == true;
            current.Automation.DualStockSyncIntervalSeconds = int.TryParse(TxtDualStockSyncInterval.Text, out var dualInterval)
                ? Math.Clamp(dualInterval, 5, 3600)
                : 15;
            current.Automation.DualStockDailySyncTime = string.IsNullOrWhiteSpace(TxtDualStockDailySyncTime.Text)
                ? "21:00"
                : TxtDualStockDailySyncTime.Text.Trim();
            current.Automation.Templates ??= new List<AutomationTemplate>();
            current.Automation.Rules ??= new List<AutomationRule>();

            current.PosDb.DatabasePath = TxtPosDbPath.Text.Trim();
            current.PosDb.AutoDetect = ChkAutoDetect.IsChecked == true;

            current.OcrReceipt.Enabled = ChkOcrEnabled.IsChecked == true;
            current.OcrReceipt.TriggerCaption = string.IsNullOrWhiteSpace(TxtOcrTriggerCaption.Text) ? "/struk" : TxtOcrTriggerCaption.Text.Trim();
            current.OcrReceipt.TessdataPath = string.IsNullOrWhiteSpace(TxtTessdataPath.Text) ? "tessdata" : TxtTessdataPath.Text.Trim();
            current.OcrReceipt.ProductMappings = _ocrProductMappings
                .Select(mapping => new OcrProductMapping
                {
                    SupplierKey = ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey),
                    InvoiceName = mapping.InvoiceName,
                    NormalizedInvoiceName = ConfigService.NormalizeOcrName(mapping.InvoiceName),
                    DatabaseProductId = mapping.DatabaseProductId,
                    DatabaseProductName = mapping.DatabaseProductName,
                    Source = mapping.Source,
                    TrustLevel = mapping.TrustLevel,
                    Confidence = mapping.Confidence,
                    CreatedAt = mapping.CreatedAt,
                    UpdatedAt = mapping.UpdatedAt,
                    LastSeenAt = mapping.LastSeenAt,
                    LastConfirmedAt = mapping.LastConfirmedAt,
                    Note = mapping.Note
                })
                .ToList();

            current.Notifications.StockThresholds = (current.Notifications.StockThresholds ?? new List<StockThreshold>())
                .Select(threshold => new StockThreshold
                {
                    Level = threshold.Level,
                    Priority = threshold.Priority
                })
                .ToList();
            current.Notifications.ExpiryThresholds = (current.Notifications.ExpiryThresholds ?? new List<ExpiryThreshold>())
                .Select(threshold => new ExpiryThreshold
                {
                    DaysBefore = threshold.DaysBefore,
                    Priority = threshold.Priority
                })
                .ToList();

            current.GoogleSheets.Enabled = ChkSheetsEnabled.IsChecked == true;
            current.GoogleSheets.CredentialsJsonPath = TxtSheetsCredentialPath.Text.Trim();
            current.GoogleSheets.SpreadsheetId = TxtSheetsSpreadsheetId.Text.Trim();
            current.GoogleSheets.PurchaseSheetName = string.IsNullOrWhiteSpace(TxtSheetsPurchaseTabName.Text) ? "Pembelian" : TxtSheetsPurchaseTabName.Text.Trim();

            return current;
        }

        private void ValidateForm()
        {
            var draft = BuildDraftConfig();

            TxtGroqValidation.Text = string.IsNullOrWhiteSpace(draft.Groq?.ApiKey) || draft.Groq.ApiKey == "YOUR_GROQ_API_KEY"
                ? "Groq belum siap: API key belum diisi."
                : ChkEnableFallback.IsChecked == true && string.IsNullOrWhiteSpace(draft.Groq?.FallbackApiKey)
                    ? $"Groq siap. Model {draft.Groq?.Model}, tetapi fallback Gemini aktif tanpa API key."
                    : $"Groq siap. Model {draft.Groq?.Model}, fallback {(ChkEnableFallback.IsChecked == true ? "aktif" : "nonaktif")}.";

            string? telegramError = TelegramBotService.ValidateBotToken(draft.Telegram?.BotToken);
            TxtTelegramValidation.Text = !string.IsNullOrWhiteSpace(telegramError)
                ? $"Telegram belum siap: {telegramError}"
                : $"Telegram siap. Owner: {draft.Telegram?.OwnerChatIds?.Count ?? 0}, Kasir: {draft.Telegram?.KasirChatIds?.Count ?? 0}.";

            TxtWhatsAppValidation.Text = BuildWhatsAppValidation(draft);
            TxtBaileysValidation.Text = BuildBaileysValidation(draft);

            bool tunnelEnabled = draft.Tunnel?.Enabled == true;
            TxtTunnelValidation.Text = !tunnelEnabled
                ? "Tunnel nonaktif. Isi Public Base URL manual jika webhook tetap perlu diakses dari internet."
                : string.IsNullOrWhiteSpace(draft.Tunnel?.BinaryPath) && !string.Equals(draft.Tunnel?.Provider, "manual", StringComparison.OrdinalIgnoreCase)
                    ? "Tunnel aktif tetapi binary path belum diisi."
                    : $"Tunnel siap. Provider: {draft.Tunnel?.Provider}.";

            bool autoDetect = draft.PosDb?.AutoDetect == true;
            TxtDatabaseValidation.Text = autoDetect
                ? "Database memakai auto-detect. Jika gagal ditemukan, isi path manual."
                : string.IsNullOrWhiteSpace(draft.PosDb?.DatabasePath)
                    ? "Database belum siap: path pos.db kosong."
                    : $"Database akan memakai path: {draft.PosDb?.DatabasePath}";

            TxtOcrValidation.Text = BuildOcrValidation(draft);
            TxtSheetsValidation.Text = BuildSheetsValidation(draft);
        }

        private static string BuildWhatsAppValidation(AppConfig draft)
        {
            string mode = WhatsAppModes.Normalize(draft.WhatsApp?.Mode);
            if (draft.WhatsApp?.Enabled != true)
            {
                return "WhatsApp nonaktif.";
            }

            var missing = new List<string>();
            var warnings = new List<string>();
            if (WhatsAppModes.UsesCloudApi(mode))
            {
                if (string.IsNullOrWhiteSpace(draft.WhatsApp?.AccessToken)) missing.Add("Access Token");
                if (string.IsNullOrWhiteSpace(draft.WhatsApp?.PhoneNumberId)) missing.Add("Phone Number ID");
                if (string.IsNullOrWhiteSpace(draft.WhatsApp?.VerifyToken)) missing.Add("Verify Token");
                if (string.IsNullOrWhiteSpace(draft.WhatsApp?.AppSecret)) warnings.Add("App Secret kosong: boleh untuk lokal/test, belum production-ready");
                if (string.IsNullOrWhiteSpace(draft.WhatsApp?.PublicWebhookUrl) && !(draft.Tunnel?.Enabled == true || !string.IsNullOrWhiteSpace(draft.Tunnel?.PublicUrl)))
                {
                    warnings.Add($"webhook masih lokal di http://localhost:{draft.WhatsApp?.LocalWebhookPort ?? 8090}/whatsapp/webhook");
                }
                if ((draft.WhatsApp?.OwnerNumbers?.Count ?? 0) == 0 && (draft.WhatsApp?.KasirNumbers?.Count ?? 0) == 0)
                {
                    missing.Add("Owner/Kasir Numbers");
                }

                if (draft.WhatsApp?.EnableTemplateMessages == true &&
                    draft.WhatsApp.TemplateMappings?.Any(mapping => !string.IsNullOrWhiteSpace(mapping.TemplateName)) != true)
                {
                    missing.Add("Template mappings");
                }
                else if (draft.WhatsApp?.EnableTemplateMessages != true)
                {
                    warnings.Add("template WA OFF: pesan proaktif Cloud API akan dilewati untuk menghindari biaya/penolakan 24 jam");
                }
            }

            string warningText = warnings.Any() ? $" Catatan: {string.Join("; ", warnings)}." : "";
            return missing.Any()
                ? $"WhatsApp mode {mode}: belum siap. Kurang: {string.Join(", ", missing)}.{warningText}"
                : $"WhatsApp mode {mode}: siap untuk lokal. Webhook lokal di port {draft.WhatsApp?.LocalWebhookPort}.{warningText}";
        }

        private static string BuildBaileysValidation(AppConfig draft)
        {
            string mode = WhatsAppModes.Normalize(draft.WhatsApp?.Mode);
            if (draft.Baileys?.Enabled != true || !WhatsAppModes.UsesBaileys(mode))
            {
                return "Baileys nonaktif.";
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(draft.Baileys.NodeBinaryPath)) missing.Add("Node Binary Path");
            if (string.IsNullOrWhiteSpace(draft.Baileys.SidecarEntryPath)) missing.Add("Sidecar Entry Path");
            if (string.IsNullOrWhiteSpace(draft.Baileys.SessionPath)) missing.Add("Session Path");
            if (string.IsNullOrWhiteSpace(draft.Baileys.BotPhoneNumber)) missing.Add("Bot Phone Number");
            if ((draft.Baileys.OwnerNumbers?.Count ?? 0) == 0 && (draft.Baileys.KasirNumbers?.Count ?? 0) == 0)
            {
                missing.Add("Owner/Kasir Numbers");
            }

            return missing.Any()
                ? $"Baileys belum siap. Kurang: {string.Join(", ", missing)}."
                : $"Baileys siap. API lokal di port {draft.Baileys.LocalApiPort}.";
        }

        private string BuildOcrValidation(AppConfig draft)
        {
            if (draft.OcrReceipt?.Enabled != true)
            {
                return "OCR nonaktif.";
            }

            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(draft.OcrReceipt.TriggerCaption))
            {
                warnings.Add("caption trigger kosong");
            }

            if (string.IsNullOrWhiteSpace(draft.OcrReceipt.TessdataPath))
            {
                warnings.Add("tessdata path kosong");
            }
            else if (!Directory.Exists(GetAbsoluteDraftPath(draft.OcrReceipt.TessdataPath)))
            {
                warnings.Add("folder tessdata belum ditemukan");
            }

            if (_posDbService == null)
            {
                warnings.Add("service pos.db belum aktif untuk pencarian produk");
            }

            if (warnings.Any())
            {
                return $"OCR aktif, tetapi perlu dicek: {string.Join(", ", warnings)}.";
            }

            int trusted = _ocrProductMappings.Count(mapping =>
                string.Equals(mapping.TrustLevel, "trusted", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(mapping.TrustLevel));
            int candidate = _ocrMappingRegistryRows.Count(row =>
                string.Equals(row.TrustLevel, "candidate", StringComparison.OrdinalIgnoreCase));
            int blocked = _ocrProductMappings.Count(mapping =>
                string.Equals(mapping.TrustLevel, "blocked", StringComparison.OrdinalIgnoreCase));
            int runtimeAliases = _ocrMappingRegistryRows.Count(row => row.IsRuntimeAlias);

            return $"OCR siap. Trigger {draft.OcrReceipt.TriggerCaption}, trusted {trusted}, candidate {candidate}, blocked {blocked}, alias runtime {runtimeAliases}, conversion {_unitConversionMappings.Count}. Hover status ini untuk lihat file mapping aktif.";
        }

        private string BuildSheetsValidation(AppConfig draft)
        {
            if (draft.GoogleSheets?.Enabled != true)
            {
                return "Google Sheets nonaktif.";
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(draft.GoogleSheets.CredentialsJsonPath))
            {
                missing.Add("credentials path");
            }
            else if (!File.Exists(GetAbsoluteDraftPath(draft.GoogleSheets.CredentialsJsonPath)))
            {
                missing.Add("file credentials belum ditemukan");
            }

            if (string.IsNullOrWhiteSpace(draft.GoogleSheets.SpreadsheetId))
            {
                missing.Add("spreadsheet id");
            }

            if (string.IsNullOrWhiteSpace(draft.GoogleSheets.PurchaseSheetName))
            {
                missing.Add("nama sheet pembelian");
            }

            return missing.Any()
                ? $"Google Sheets belum siap: {string.Join(", ", missing)}."
                : $"Google Sheets siap. Target tab: {draft.GoogleSheets.PurchaseSheetName}.";
        }

        private static string GetAbsoluteDraftPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath));
        }

        private void LoadOcrMappings(IEnumerable<OcrProductMapping>? mappings)
        {
            _ocrProductMappings.Clear();
            foreach (var mapping in mappings ?? Enumerable.Empty<OcrProductMapping>())
            {
                _ocrProductMappings.Add(new OcrProductMapping
                {
                    SupplierKey = ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey),
                    InvoiceName = mapping.InvoiceName,
                    NormalizedInvoiceName = ConfigService.NormalizeOcrName(mapping.InvoiceName),
                    DatabaseProductId = mapping.DatabaseProductId,
                    DatabaseProductName = mapping.DatabaseProductName,
                    Source = string.IsNullOrWhiteSpace(mapping.Source) ? "legacy" : mapping.Source,
                    TrustLevel = string.IsNullOrWhiteSpace(mapping.TrustLevel) ? "trusted" : mapping.TrustLevel,
                    Confidence = mapping.Confidence,
                    CreatedAt = mapping.CreatedAt,
                    UpdatedAt = mapping.UpdatedAt,
                    LastSeenAt = mapping.LastSeenAt,
                    LastConfirmedAt = mapping.LastConfirmedAt,
                    Note = mapping.Note
                });
            }

            RebuildOcrMappingRegistryRows();
        }

        private void UpsertOcrMappingInGrid(string invoiceName, string dbProductId, string dbProductName, string? supplierKey = null, string source = "manual", string trustLevel = "trusted")
        {
            string normalizedInvoiceName = invoiceName.Trim();
            string normalizedSupplierKey = ConfigService.NormalizeOcrSupplierKey(supplierKey);
            var existing = _ocrProductMappings.FirstOrDefault(mapping =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey), normalizedSupplierKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(mapping.InvoiceName), ConfigService.NormalizeOcrName(normalizedInvoiceName), StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.SupplierKey = normalizedSupplierKey;
                existing.InvoiceName = normalizedInvoiceName;
                existing.NormalizedInvoiceName = ConfigService.NormalizeOcrName(normalizedInvoiceName);
                existing.DatabaseProductId = dbProductId;
                existing.DatabaseProductName = dbProductName;
                existing.Source = source;
                existing.TrustLevel = trustLevel;
                existing.UpdatedAt = DateTime.Now;
                RebuildOcrMappingRegistryRows();
                return;
            }

            _ocrProductMappings.Add(new OcrProductMapping
            {
                SupplierKey = normalizedSupplierKey,
                InvoiceName = normalizedInvoiceName,
                NormalizedInvoiceName = ConfigService.NormalizeOcrName(normalizedInvoiceName),
                DatabaseProductId = dbProductId,
                DatabaseProductName = dbProductName,
                Source = source,
                TrustLevel = trustLevel,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                LastConfirmedAt = string.Equals(trustLevel, "trusted", StringComparison.OrdinalIgnoreCase) ? DateTime.Now : null
            });
            RebuildOcrMappingRegistryRows();
        }

        private void ClearMappingInputs()
        {
            _selectedMappingProductId = null;
            TxtMappingInvoiceName.Text = string.Empty;
            TxtMappingDbName.Text = string.Empty;
            SelectComboItem(CmbMappingSupplier, "GLOBAL");
            SelectComboItem(CmbMappingStatus, "trusted");
        }

        private void PersistCurrentOcrMappings()
        {
            _configService.ReplaceOcrMappings(_ocrProductMappings);
            RebuildOcrMappingRegistryRows();
            TxtOcrValidation.Text = BuildOcrValidation(BuildDraftConfig());
            TxtOcrValidation.ToolTip = _configService.OcrMappingsPath;
        }

        private void RebuildOcrMappingRegistryRows(IEnumerable<ProductAliasEntry>? aliases = null)
        {
            var existingAliases = aliases?.ToList();
            _ocrMappingRegistryRows.Clear();
            foreach (var mapping in _ocrProductMappings)
            {
                _ocrMappingRegistryRows.Add(new OcrMappingRegistryRow
                {
                    SupplierKey = ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey),
                    InvoiceName = mapping.InvoiceName,
                    DatabaseProductId = mapping.DatabaseProductId,
                    DatabaseProductName = mapping.DatabaseProductName,
                    Source = string.IsNullOrWhiteSpace(mapping.Source) ? "legacy" : mapping.Source,
                    TrustLevel = string.IsNullOrWhiteSpace(mapping.TrustLevel) ? "trusted" : mapping.TrustLevel,
                    UpdatedAt = mapping.UpdatedAt,
                    IsRuntimeAlias = false
                });
            }

            if (existingAliases == null)
            {
                return;
            }

            foreach (var alias in existingAliases)
            {
                bool alreadyVisible = _ocrProductMappings.Any(mapping =>
                    string.Equals(ConfigService.NormalizeOcrName(mapping.InvoiceName), ConfigService.NormalizeOcrName(alias.AliasName), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(mapping.DatabaseProductId, alias.ProductId, StringComparison.OrdinalIgnoreCase));
                if (alreadyVisible)
                {
                    continue;
                }

                _ocrMappingRegistryRows.Add(new OcrMappingRegistryRow
                {
                    SupplierKey = "GLOBAL",
                    InvoiceName = alias.AliasName,
                    DatabaseProductId = alias.ProductId,
                    DatabaseProductName = alias.ProductName ?? "",
                    Source = string.IsNullOrWhiteSpace(alias.Source) ? "legacy-alias" : alias.Source,
                    TrustLevel = "candidate",
                    UpdatedAt = alias.UpdatedAt,
                    IsRuntimeAlias = true
                });
            }
        }

        private async Task RefreshOcrMappingRegistryAsync()
        {
            try
            {
                var aliases = await _databaseService.GetProductAliasesAsync();
                RebuildOcrMappingRegistryRows(aliases);
                TxtOcrValidation.Text = BuildOcrValidation(BuildDraftConfig());
            }
            catch
            {
                RebuildOcrMappingRegistryRows();
            }
        }

        private void ClearUnitConversionInputs()
        {
            _selectedUnitConversionId = null;
            _selectedParentConversionProductId = null;
            _selectedChildConversionProductId = null;
            TxtConversionParentName.Text = string.Empty;
            TxtConversionChildName.Text = string.Empty;
            TxtConversionRate.Text = string.Empty;
        }

        private async Task RefreshUnitConversionsAsync()
        {
            try
            {
                var items = await _databaseService.GetAllUnitConversionsAsync();
                _unitConversionMappings.Clear();
                foreach (var item in items)
                {
                    _unitConversionMappings.Add(item);
                }

                TxtUnitConversionStatus.Text = items.Count == 0
                    ? "Belum ada mapping unit conversion."
                    : $"{items.Count} mapping unit conversion aktif.";
                TxtOcrValidation.Text = BuildOcrValidation(BuildDraftConfig());
            }
            catch (Exception ex)
            {
                TxtUnitConversionStatus.Text = $"Gagal memuat unit conversion: {ex.Message}";
            }
        }

        private async Task SearchConversionProductAsync(bool isParent)
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowError("Unit Conversion", "Database pos.db belum siap untuk pencarian produk.");
                return;
            }

            var products = await _posDbService.GetAllProductsAsync();
            if (!products.Any())
            {
                ToastHelper.ShowError("Unit Conversion", "Tidak ada produk yang bisa dipilih dari database.");
                return;
            }

            string seed = isParent ? TxtConversionParentName.Text.Trim() : TxtConversionChildName.Text.Trim();
            var picker = new ProductSearchWindow(products, seed)
            {
                Owner = Window.GetWindow(this)
            };

            if (picker.ShowDialog() == true && picker.SelectedProduct != null)
            {
                if (isParent)
                {
                    _selectedParentConversionProductId = picker.SelectedProduct.Id;
                    TxtConversionParentName.Text = picker.SelectedProduct.Name ?? string.Empty;
                }
                else
                {
                    _selectedChildConversionProductId = picker.SelectedProduct.Id;
                    TxtConversionChildName.Text = picker.SelectedProduct.Name ?? string.Empty;
                }
            }
        }

        private async Task ApplyShadowConversionFromSettingsAsync(
            string parentProductId,
            decimal parentQuantity,
            int? isiPerBox = null,
            string? parentProductName = null,
            decimal? parentUnitCost = null)
        {
            if (_posDbService == null || string.IsNullOrWhiteSpace(parentProductId) || parentQuantity <= 0)
            {
                return;
            }

            UnitConversionMapping? conversion = await _databaseService.GetConversionByParentIdAsync(parentProductId);
            if (conversion == null ||
                string.Equals(conversion.ParentProductId, conversion.ChildProductId, StringComparison.OrdinalIgnoreCase))
            {
                if (isiPerBox.GetValueOrDefault() <= 0)
                {
                    ToastHelper.ShowInfo(
                        "Shadow Conversion",
                        "Produk ini belum punya unit conversion mapping. Buat mapping di tab Unit Conversion jika perlu stok ecer ikut bertambah.");
                    return;
                }

                var owner = Window.GetWindow(this);
                var confirm = owner != null
                    ? MessageBox.Show(
                        owner,
                        $"Produk ini belum punya shadow mapping.\n\nParent: {parentProductName ?? parentProductId}\nIsi per box dari invoice: {isiPerBox}\n\nPilih produk eceran/child dan simpan mapping otomatis?",
                        "Shadow Conversion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                    : MessageBox.Show(
                        $"Produk ini belum punya shadow mapping.\n\nParent: {parentProductName ?? parentProductId}\nIsi per box dari invoice: {isiPerBox}\n\nPilih produk eceran/child dan simpan mapping otomatis?",
                        "Shadow Conversion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                var products = await _posDbService.GetAllProductsAsync();
                var picker = new ProductSearchWindow(products, parentProductName ?? string.Empty)
                {
                    Owner = owner
                };

                if (picker.ShowDialog() != true || picker.SelectedProduct == null || string.IsNullOrWhiteSpace(picker.SelectedProduct.Id))
                {
                    return;
                }

                conversion = new UnitConversionMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ParentProductId = parentProductId,
                    ParentProductName = parentProductName,
                    ChildProductId = picker.SelectedProduct.Id,
                    ChildProductName = picker.SelectedProduct.Name,
                    ConversionRate = isiPerBox!.Value,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _databaseService.UpsertUnitConversionAsync(conversion);
                await RefreshUnitConversionsAsync();
            }

            decimal effectiveRate = isiPerBox.GetValueOrDefault() > 0
                ? isiPerBox!.Value
                : conversion.ConversionRate;
            if (effectiveRate <= 0)
            {
                return;
            }

            decimal childQuantity = parentQuantity * effectiveRate;
            decimal? childUnitCost = parentUnitCost.GetValueOrDefault() > 0 && effectiveRate > 0
                ? Math.Round(parentUnitCost!.Value / effectiveRate, 2, MidpointRounding.AwayFromZero)
                : null;
            decimal childUnitCostValue = childUnitCost.GetValueOrDefault();
            bool hasChildUnitCost = childUnitCostValue > 0;
            bool updateMasterCost = false;
            if (hasChildUnitCost)
            {
                var childProduct = await _posDbService.GetProductByIdAsync(conversion.ChildProductId);
                decimal oldCost = childProduct?.PurchasePrice ?? 0;
                bool shouldPromptCostChange = oldCost <= 0 ||
                    (Math.Abs(childUnitCostValue - oldCost) >= 1 && oldCost > 0 && Math.Abs((childUnitCostValue - oldCost) / oldCost * 100) >= 1);

                if (shouldPromptCostChange)
                {
                    string direction = childUnitCostValue >= oldCost ? "naik" : "turun";
                    string percent = oldCost > 0
                        ? $"{((childUnitCostValue - oldCost) / oldCost * 100):+0.##;-0.##;0}%"
                        : "baru";
                    var confirmCost = MessageBox.Show(
                        Window.GetWindow(this),
                        $"Modal child berubah.\n\nProduk: {conversion.ChildProductName ?? conversion.ChildProductId}\nModal DB lama: Rp {oldCost:N0}\nModal baru: Rp {childUnitCostValue:N0}\nPerubahan: {direction} {percent}\n\nUpdate master modal produk child?\n\nYes = update Product.Cost\nNo = dokumen shadow memakai modal baru, tapi master modal tidak diubah\nCancel = batalkan shadow conversion",
                        "Konfirmasi Modal Shadow",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (confirmCost == MessageBoxResult.Cancel)
                    {
                        return;
                    }

                    updateMasterCost = confirmCost == MessageBoxResult.Yes;
                }
            }

            string? internalNote = hasChildUnitCost
                ? $"SSA shadow conversion | Parent {parentProductName ?? parentProductId} | {parentQuantity:N2} x {effectiveRate:N2} -> {childQuantity:N2} {conversion.ChildProductName ?? conversion.ChildProductId} | modal Rp {childUnitCostValue:N0}"
                : null;
            var adjustResult = hasChildUnitCost
                ? await _posDbService.AdjustStockWithCostAsync(conversion.ChildProductId, childQuantity, childUnitCostValue, updateMasterCost: updateMasterCost, internalNote: internalNote)
                : await _posDbService.AdjustStockAsync(conversion.ChildProductId, childQuantity);
            if (!adjustResult.Success)
            {
                ToastHelper.ShowWarning(
                    "Shadow Conversion",
                    adjustResult.Error ?? $"Gagal menerapkan konversi ke {conversion.ChildProductName ?? conversion.ChildProductId}.");
                return;
            }

            ToastHelper.ShowSuccess(
                "Shadow Conversion",
                $"+{childQuantity:N2} {conversion.ChildProductName ?? conversion.ChildProductId} ditambahkan dari {(isiPerBox.GetValueOrDefault() > 0 ? "isi invoice" : "mapping")} {conversion.ParentProductName ?? parentProductId}{(hasChildUnitCost ? $" | modal child Rp {childUnitCostValue:N0} (total Rp {(childUnitCostValue * childQuantity):N0})" : string.Empty)}.");
        }

        private bool ConfirmOcrReviewResolution(OcrReviewQueueItem queueItem, Product selectedProduct)
        {
            string rawName = queueItem.RawProductName?.Trim() ?? string.Empty;
            string selectedProductId = selectedProduct.Id?.Trim() ?? string.Empty;
            string selectedProductName = selectedProduct.Name?.Trim() ?? string.Empty;

            var existingMapping = _ocrProductMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.InvoiceName, rawName, StringComparison.OrdinalIgnoreCase));

            string mappingStatus;
            if (existingMapping == null)
            {
                mappingStatus = "Status mapping: akan dibuat baru.";
            }
            else if (string.Equals(existingMapping.DatabaseProductId, selectedProductId, StringComparison.OrdinalIgnoreCase))
            {
                mappingStatus = $"Status mapping: sudah mengarah ke produk yang sama ({existingMapping.DatabaseProductName}).";
            }
            else
            {
                mappingStatus = $"Status mapping: akan menimpa mapping lama {existingMapping.DatabaseProductName} (ID {existingMapping.DatabaseProductId}).";
            }

            string message =
                "Konfirmasi hasil OCR review.\n\n" +
                $"Nama OCR: {rawName}\n" +
                $"Supplier: {queueItem.SupplierName}\n" +
                $"Qty: {queueItem.Quantity:N2}\n" +
                $"Harga: {queueItem.UnitPrice:N0}\n\n" +
                $"Produk tujuan: {selectedProductName} (ID {selectedProductId})\n" +
                $"{mappingStatus}\n\n" +
                "Lanjutkan proses dan simpan ke pemetaan produk?";

            var owner = Window.GetWindow(this);
            MessageBoxResult result = owner != null
                ? MessageBox.Show(owner, message, "Konfirmasi OCR Review", MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(message, "Konfirmasi OCR Review", MessageBoxButton.YesNo, MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        private async Task RefreshOcrReviewQueueAsync()
        {
            try
            {
                var items = await _databaseService.GetPendingOcrReviewQueueItemsAsync();
                _ocrReviewQueueItems.Clear();
                foreach (var item in items)
                {
                    _ocrReviewQueueItems.Add(item);
                }

                TxtOcrQueueStatus.Text = items.Count == 0
                    ? "Tidak ada item yang menunggu review."
                    : $"{items.Count} item menunggu review OCR.";
            }
            catch (Exception ex)
            {
                TxtOcrQueueStatus.Text = $"Gagal memuat OCR Review Queue: {ex.Message}";
            }
        }

        private async Task<T> RunWithDraftConfigAsync<T>(Func<Task<T>> action)
        {
            AppConfig previous = _configService.CloneConfig();
            AppConfig draft = BuildDraftConfig();
            _configService.ReplaceInMemoryConfig(draft, save: false);
            try
            {
                return await action();
            }
            finally
            {
                _configService.ReplaceInMemoryConfig(previous, save: false);
            }
        }

        private void BtnShowGroqKey_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _groqKeyVisible, TxtGroqApiKey, OverlayGroqApiKey, TxtGroqApiKeyMasked, BtnShowGroqKey);

        private void BtnShowGeminiKey_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _geminiKeyVisible, TxtGeminiApiKey, OverlayGeminiApiKey, TxtGeminiApiKeyMasked, BtnShowGeminiKey);

        private void BtnShowBotToken_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _botTokenVisible, TxtBotToken, OverlayBotToken, TxtBotTokenMasked, BtnShowBotToken);

        private void BtnShowWhatsAppAccessToken_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _whatsAppAccessTokenVisible, TxtWhatsAppAccessToken, OverlayWhatsAppAccessToken, TxtWhatsAppAccessTokenMasked, BtnShowWhatsAppAccessToken);

        private void BtnShowWhatsAppAppSecret_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _whatsAppAppSecretVisible, TxtWhatsAppAppSecret, OverlayWhatsAppAppSecret, TxtWhatsAppAppSecretMasked, BtnShowWhatsAppAppSecret);

        private void BtnShowWhatsAppVerifyToken_Click(object sender, RoutedEventArgs e) =>
            ToggleSecret(ref _whatsAppVerifyTokenVisible, TxtWhatsAppVerifyToken, OverlayWhatsAppVerifyToken, TxtWhatsAppVerifyTokenMasked, BtnShowWhatsAppVerifyToken);

        private void ChkEnableFallback_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkEnableFallback.IsChecked == true && string.IsNullOrWhiteSpace(TxtGeminiApiKey.Text))
            {
                _geminiKeyVisible = true;
            }

            UpdateEnabledStates();
        }
        private void ChkWhatsAppEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
        private void ChkBaileysEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
        private void ChkTunnelEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
        private void ChkAutoDetect_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
        private void ChkOcrEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
        private void ChkSheetsEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();

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

        private void BtnBrowseTessdata_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Pilih folder tessdata"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtTessdataPath.Text = dialog.FolderName;
            }
        }

        private void BtnBrowseSheetsCredential_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON File (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Pilih service account credentials JSON"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtSheetsCredentialPath.Text = dialog.FileName;
            }
        }

        private async void BtnSearchDbProduct_Click(object sender, RoutedEventArgs e)
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowError("OCR Mapping", "Database pos.db belum siap untuk pencarian produk.");
                return;
            }

            BtnSearchDbProduct.IsEnabled = false;
            try
            {
                var products = await _posDbService.GetAllProductsAsync();
                if (!products.Any())
                {
                    ToastHelper.ShowError("OCR Mapping", "Tidak ada produk yang bisa dipilih dari database.");
                    return;
                }

                var picker = new ProductSearchWindow(products, TxtMappingInvoiceName.Text.Trim())
                {
                    Owner = Window.GetWindow(this)
                };

                if (picker.ShowDialog() == true && picker.SelectedProduct != null)
                {
                    _selectedMappingProductId = picker.SelectedProduct.Id;
                    TxtMappingDbName.Text = picker.SelectedProduct.Name ?? "";
                    MarkDirty();
                }
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("OCR Mapping", ex.Message);
            }
            finally
            {
                BtnSearchDbProduct.IsEnabled = ChkOcrEnabled.IsChecked == true && _posDbService != null;
            }
        }

        private async void BtnAddMapping_Click(object sender, RoutedEventArgs e)
        {
            string invoiceName = TxtMappingInvoiceName.Text.Trim();
            string databaseProductName = TxtMappingDbName.Text.Trim();
            string supplierKey = ConfigService.NormalizeOcrSupplierKey(GetComboValue(CmbMappingSupplier, "GLOBAL"));
            string trustLevel = GetComboValue(CmbMappingStatus, "trusted");

            if (string.IsNullOrWhiteSpace(invoiceName))
            {
                ToastHelper.ShowError("OCR Mapping", "Isi nama produk di faktur terlebih dahulu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedMappingProductId) || string.IsNullOrWhiteSpace(databaseProductName))
            {
                ToastHelper.ShowError("OCR Mapping", "Pilih produk database terlebih dahulu.");
                return;
            }

            var existing = _ocrProductMappings.FirstOrDefault(mapping =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey), supplierKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(mapping.InvoiceName), ConfigService.NormalizeOcrName(invoiceName), StringComparison.OrdinalIgnoreCase));

            DateTime now = DateTime.Now;
            if (existing != null)
            {
                existing.SupplierKey = supplierKey;
                existing.DatabaseProductId = _selectedMappingProductId;
                existing.DatabaseProductName = databaseProductName;
                existing.InvoiceName = invoiceName;
                existing.NormalizedInvoiceName = ConfigService.NormalizeOcrName(invoiceName);
                existing.Source = "manual";
                existing.TrustLevel = trustLevel;
                existing.UpdatedAt = now;
                if (string.Equals(trustLevel, "trusted", StringComparison.OrdinalIgnoreCase))
                {
                    existing.LastConfirmedAt = now;
                }
            }
            else
            {
                _ocrProductMappings.Add(new OcrProductMapping
                {
                    SupplierKey = supplierKey,
                    InvoiceName = invoiceName,
                    NormalizedInvoiceName = ConfigService.NormalizeOcrName(invoiceName),
                    DatabaseProductId = _selectedMappingProductId,
                    DatabaseProductName = databaseProductName,
                    Source = "manual",
                    TrustLevel = trustLevel,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastConfirmedAt = string.Equals(trustLevel, "trusted", StringComparison.OrdinalIgnoreCase) ? now : null
                });
            }

            PersistCurrentOcrMappings();
            if (string.Equals(trustLevel, "trusted", StringComparison.OrdinalIgnoreCase))
            {
                await _databaseService.UpsertProductAliasAsync(new ProductAliasEntry
                {
                    AliasName = invoiceName,
                    ProductId = _selectedMappingProductId,
                    ProductName = databaseProductName,
                    Source = "config-mapping",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await RefreshOcrMappingRegistryAsync();
            ClearMappingInputs();
            MarkDirty();
        }

        private async void BtnDeleteMapping_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not OcrMappingRegistryRow row)
            {
                return;
            }

            var mapping = _ocrProductMappings.FirstOrDefault(item =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(item.SupplierKey), ConfigService.NormalizeOcrSupplierKey(row.SupplierKey), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(item.InvoiceName), ConfigService.NormalizeOcrName(row.InvoiceName), StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                _ocrProductMappings.Remove(mapping);
                PersistCurrentOcrMappings();
            }

            await _databaseService.DeleteProductAliasAsync(row.InvoiceName);
            await RefreshOcrMappingRegistryAsync();
            MarkDirty();
        }

        private void BtnEditMapping_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not OcrMappingRegistryRow row)
            {
                return;
            }

            var mapping = _ocrProductMappings.FirstOrDefault(item =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(item.SupplierKey), ConfigService.NormalizeOcrSupplierKey(row.SupplierKey), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(item.InvoiceName), ConfigService.NormalizeOcrName(row.InvoiceName), StringComparison.OrdinalIgnoreCase));

            _selectedMappingProductId = mapping?.DatabaseProductId ?? row.DatabaseProductId;
            TxtMappingInvoiceName.Text = mapping?.InvoiceName ?? row.InvoiceName;
            TxtMappingDbName.Text = mapping?.DatabaseProductName ?? row.DatabaseProductName;
            SelectComboItem(CmbMappingSupplier, ConfigService.NormalizeOcrSupplierKey(mapping?.SupplierKey ?? row.SupplierKey));
            SelectComboItem(CmbMappingStatus, string.IsNullOrWhiteSpace(mapping?.TrustLevel) ? row.TrustLevel : mapping.TrustLevel);
            TxtMappingInvoiceName.Focus();
            TxtMappingInvoiceName.SelectAll();
        }

        private async void BtnBlockMapping_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not OcrMappingRegistryRow row)
            {
                return;
            }

            string supplierKey = ConfigService.NormalizeOcrSupplierKey(row.SupplierKey);
            var mapping = _ocrProductMappings.FirstOrDefault(item =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(item.SupplierKey), supplierKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(item.InvoiceName), ConfigService.NormalizeOcrName(row.InvoiceName), StringComparison.OrdinalIgnoreCase));

            if (mapping == null)
            {
                mapping = new OcrProductMapping
                {
                    SupplierKey = supplierKey,
                    InvoiceName = row.InvoiceName,
                    NormalizedInvoiceName = ConfigService.NormalizeOcrName(row.InvoiceName),
                    DatabaseProductId = row.DatabaseProductId,
                    DatabaseProductName = row.DatabaseProductName,
                    Source = string.IsNullOrWhiteSpace(row.Source) ? "manual" : row.Source,
                    CreatedAt = DateTime.Now
                };
                _ocrProductMappings.Add(mapping);
            }

            mapping.TrustLevel = "blocked";
            mapping.UpdatedAt = DateTime.Now;
            mapping.Note = "Blocked dari Settings UI agar tidak auto-valid ulang.";
            PersistCurrentOcrMappings();
            await _databaseService.DeleteProductAliasAsync(row.InvoiceName);
            await RefreshOcrMappingRegistryAsync();
            MarkDirty();
        }

        private async void BtnRefreshMappings_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshMappings.IsEnabled = false;
            try
            {
                await RefreshOcrMappingRegistryAsync();
            }
            finally
            {
                BtnRefreshMappings.IsEnabled = ChkOcrEnabled.IsChecked == true;
            }
        }

        private async void BtnSearchParentConversionProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SearchConversionProductAsync(isParent: true);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Unit Conversion", ex.Message);
            }
        }

        private async void BtnSearchChildConversionProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SearchConversionProductAsync(isParent: false);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Unit Conversion", ex.Message);
            }
        }

        private async void BtnAddUnitConversion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedParentConversionProductId) || string.IsNullOrWhiteSpace(TxtConversionParentName.Text))
            {
                ToastHelper.ShowError("Unit Conversion", "Pilih produk induk terlebih dahulu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedChildConversionProductId) || string.IsNullOrWhiteSpace(TxtConversionChildName.Text))
            {
                ToastHelper.ShowError("Unit Conversion", "Pilih produk anak terlebih dahulu.");
                return;
            }

            if (string.Equals(_selectedParentConversionProductId, _selectedChildConversionProductId, StringComparison.OrdinalIgnoreCase))
            {
                ToastHelper.ShowError("Unit Conversion", "Produk induk dan produk anak tidak boleh sama.");
                return;
            }

            if (!decimal.TryParse(TxtConversionRate.Text.Trim(), out decimal conversionRate) || conversionRate <= 0)
            {
                ToastHelper.ShowError("Unit Conversion", "Rasio konversi harus angka lebih dari 0.");
                return;
            }

            try
            {
                await _databaseService.UpsertUnitConversionAsync(new UnitConversionMapping
                {
                    Id = string.IsNullOrWhiteSpace(_selectedUnitConversionId) ? Guid.NewGuid().ToString("N") : _selectedUnitConversionId,
                    ParentProductId = _selectedParentConversionProductId,
                    ParentProductName = TxtConversionParentName.Text.Trim(),
                    ChildProductId = _selectedChildConversionProductId,
                    ChildProductName = TxtConversionChildName.Text.Trim(),
                    ConversionRate = conversionRate,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });

                await RefreshUnitConversionsAsync();
                ClearUnitConversionInputs();
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Unit Conversion", ex.Message);
            }
        }

        private void BtnEditUnitConversion_Click(object sender, RoutedEventArgs e)
        {
            string? mappingId = (sender as FrameworkElement)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(mappingId))
            {
                return;
            }

            UnitConversionMapping? mapping = _unitConversionMappings.FirstOrDefault(item =>
                string.Equals(item.Id, mappingId, StringComparison.OrdinalIgnoreCase));
            if (mapping == null)
            {
                return;
            }

            _selectedUnitConversionId = mapping.Id;
            _selectedParentConversionProductId = mapping.ParentProductId;
            _selectedChildConversionProductId = mapping.ChildProductId;
            TxtConversionParentName.Text = mapping.ParentProductName ?? string.Empty;
            TxtConversionChildName.Text = mapping.ChildProductName ?? string.Empty;
            TxtConversionRate.Text = mapping.ConversionRate.ToString("0.##");
            TxtConversionRate.Focus();
            TxtConversionRate.SelectAll();
        }

        private async void BtnDeleteUnitConversion_Click(object sender, RoutedEventArgs e)
        {
            string? mappingId = (sender as FrameworkElement)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(mappingId))
            {
                return;
            }

            try
            {
                await _databaseService.DeleteUnitConversionAsync(mappingId);
                await RefreshUnitConversionsAsync();
                if (string.Equals(_selectedUnitConversionId, mappingId, StringComparison.OrdinalIgnoreCase))
                {
                    ClearUnitConversionInputs();
                }
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Unit Conversion", ex.Message);
            }
        }

        private async void BtnRefreshUnitConversions_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshUnitConversions.IsEnabled = false;
            try
            {
                await RefreshUnitConversionsAsync();
            }
            finally
            {
                BtnRefreshUnitConversions.IsEnabled = true;
            }
        }

        private async void BtnRefreshOcrQueue_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshOcrQueue.IsEnabled = false;
            try
            {
                await RefreshOcrReviewQueueAsync();
            }
            finally
            {
                BtnRefreshOcrQueue.IsEnabled = true;
            }
        }

        private async void BtnResolveOcrQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_posDbService == null)
            {
                ToastHelper.ShowError("OCR Review", "Database pos.db belum siap.");
                return;
            }

            if (!long.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out long itemId))
            {
                return;
            }

            var queueItem = _ocrReviewQueueItems.FirstOrDefault(item => item.Id == itemId);
            if (queueItem == null)
            {
                return;
            }

            try
            {
                var products = await _posDbService.GetAllProductsAsync();
                var picker = new ProductSearchWindow(products, queueItem.RawProductName)
                {
                    Owner = Window.GetWindow(this)
                };

                if (picker.ShowDialog() != true || picker.SelectedProduct == null)
                {
                    return;
                }

                if (!ConfirmOcrReviewResolution(queueItem, picker.SelectedProduct))
                {
                    return;
                }

                if (!int.TryParse(picker.SelectedProduct.Id, out int productId))
                {
                    ToastHelper.ShowError("OCR Review", "ID produk terpilih tidak valid.");
                    return;
                }

                bool isWingsQueueItem = !string.IsNullOrWhiteSpace(queueItem.SupplierName) &&
                                        (queueItem.SupplierName.Contains("WINGS", StringComparison.OrdinalIgnoreCase) ||
                                         queueItem.SupplierName.Contains("SAYAP MAS", StringComparison.OrdinalIgnoreCase));
                decimal price = isWingsQueueItem && queueItem.Quantity > 0 && queueItem.LineTotal > 0
                    ? queueItem.LineTotal / queueItem.Quantity
                    : queueItem.UnitPrice > 0
                        ? queueItem.UnitPrice
                        : queueItem.Quantity > 0 && queueItem.LineTotal > 0
                            ? queueItem.LineTotal / queueItem.Quantity
                            : picker.SelectedProduct.PurchasePrice ?? 0;

                decimal quantity = queueItem.Quantity > 0 ? queueItem.Quantity : 1;
                var result = await _posDbService.CreatePurchaseDocumentAsync(
                    productId,
                    quantity,
                    price,
                    1,
                    $"OCR Review Queue | Raw: {queueItem.RawProductName} | Supplier: {queueItem.SupplierName} | Correlation: {queueItem.ReceiptCorrelationId}");

                if (!result.Success)
                {
                    ToastHelper.ShowError("OCR Review", result.Error ?? "Gagal membuat purchase document.");
                    return;
                }

                await ApplyShadowConversionFromSettingsAsync(
                    picker.SelectedProduct.Id ?? "",
                    quantity,
                    queueItem.IsiPerBox,
                    picker.SelectedProduct.Name ?? queueItem.RawProductName,
                    price);

                await _databaseService.UpsertProductAliasAsync(new ProductAliasEntry
                {
                    AliasName = queueItem.RawProductName,
                    ProductId = picker.SelectedProduct.Id ?? "",
                    ProductName = picker.SelectedProduct.Name,
                    Source = "review-queue",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });

                await _databaseService.ResolveOcrReviewQueueItemAsync(
                    queueItem.Id,
                    picker.SelectedProduct.Id ?? "",
                    picker.SelectedProduct.Name ?? "",
                    $"Dokumen purchase: {result.DocumentNumber}");

                string supplierKey = ConfigService.NormalizeOcrSupplierKey(queueItem.SupplierName);
                _configService.AddOcrMapping(
                    queueItem.RawProductName,
                    picker.SelectedProduct.Id ?? "",
                    picker.SelectedProduct.Name ?? "",
                    supplierKey,
                    "review-queue",
                    "trusted",
                    note: $"Resolved dari OCR Review Queue item {queueItem.Id}.");
                UpsertOcrMappingInGrid(
                    queueItem.RawProductName,
                    picker.SelectedProduct.Id ?? "",
                    picker.SelectedProduct.Name ?? "",
                    supplierKey,
                    "review-queue",
                    "trusted");
                ClearMappingInputs();

                await RefreshOcrMappingRegistryAsync();
                await RefreshOcrReviewQueueAsync();
                ToastHelper.ShowSuccess("OCR Review", $"Item diproses ke dokumen {result.DocumentNumber}, alias disimpan, dan OCR mapping diperbarui.");
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("OCR Review", ex.Message);
            }
        }

        private async void BtnDeleteOcrQueue_Click(object sender, RoutedEventArgs e)
        {
            if (!long.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out long itemId))
            {
                return;
            }

            try
            {
                await _databaseService.DeleteOcrReviewQueueItemAsync(itemId);
                await RefreshOcrReviewQueueAsync();
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("OCR Review", ex.Message);
            }
        }

        private async void BtnTestGroq_Click(object sender, RoutedEventArgs e)
        {
            BtnTestGroq.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var groqService = new GroqService(_configService, _loggingService);
                    return await groqService.TestGroqConnectionAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("Groq", result.Message);
                else ToastHelper.ShowError("Groq", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Groq", ex.Message);
            }
            finally
            {
                BtnTestGroq.IsEnabled = true;
            }
        }

        private async void BtnTestGemini_Click(object sender, RoutedEventArgs e)
        {
            BtnTestGemini.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var groqService = new GroqService(_configService, _loggingService);
                    return await groqService.TestGeminiConnectionAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("Gemini", result.Message);
                else ToastHelper.ShowError("Gemini", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Gemini", ex.Message);
            }
            finally
            {
                BtnTestGemini.IsEnabled = true;
            }
        }

        private async void BtnTestWhatsAppMeta_Click(object sender, RoutedEventArgs e)
        {
            BtnTestWhatsAppMeta.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var handler = CreateWhatsAppHandler();
                    return await handler.TestCredentialsAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("WhatsApp", result.Message);
                else ToastHelper.ShowError("WhatsApp", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("WhatsApp", ex.Message);
            }
            finally
            {
                BtnTestWhatsAppMeta.IsEnabled = true;
            }
        }

        private async void BtnTestWhatsAppWebhook_Click(object sender, RoutedEventArgs e)
        {
            BtnTestWhatsAppWebhook.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var handler = CreateWhatsAppHandler();
                    string? publicBaseUrl = !string.IsNullOrWhiteSpace(TxtTunnelPublicUrl.Text)
                        ? TxtTunnelPublicUrl.Text.Trim()
                        : TxtWhatsAppPublicUrl.Text.Trim();
                    return await handler.TestWebhookReadinessAsync(publicBaseUrl);
                });

                if (result.Success) ToastHelper.ShowSuccess("Webhook", result.Message);
                else ToastHelper.ShowError("Webhook", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Webhook", ex.Message);
            }
            finally
            {
                BtnTestWhatsAppWebhook.IsEnabled = true;
            }
        }

        private async void BtnTestWhatsAppSend_Click(object sender, RoutedEventArgs e)
        {
            BtnTestWhatsAppSend.IsEnabled = false;
            try
            {
                string recipient = ParseWhatsAppNumbers(TxtWhatsAppOwnerNumbers.Text).FirstOrDefault()
                    ?? ParseWhatsAppNumbers(TxtWhatsAppKasirNumbers.Text).FirstOrDefault()
                    ?? "";

                if (string.IsNullOrWhiteSpace(recipient))
                {
                    ToastHelper.ShowError("WhatsApp", "Isi minimal satu nomor owner atau kasir untuk test outbound.");
                    return;
                }

                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var handler = CreateWhatsAppHandler();
                    return await handler.TestOutboundAsync(recipient, $"Test outbound SSA {DateTime.Now:dd/MM HH:mm:ss}");
                });

                if (result.Success) ToastHelper.ShowSuccess("WhatsApp", result.Message);
                else ToastHelper.ShowError("WhatsApp", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("WhatsApp", ex.Message);
            }
            finally
            {
                BtnTestWhatsAppSend.IsEnabled = true;
            }
        }

        private async void BtnTestWhatsAppTemplate_Click(object sender, RoutedEventArgs e)
        {
            BtnTestWhatsAppTemplate.IsEnabled = false;
            try
            {
                string recipient = ParseWhatsAppNumbers(TxtWhatsAppOwnerNumbers.Text).FirstOrDefault()
                    ?? ParseWhatsAppNumbers(TxtWhatsAppKasirNumbers.Text).FirstOrDefault()
                    ?? "";

                if (string.IsNullOrWhiteSpace(recipient))
                {
                    ToastHelper.ShowError("WhatsApp", "Isi minimal satu nomor owner atau kasir untuk test template.");
                    return;
                }

                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var handler = CreateWhatsAppHandler();
                    return await handler.TestTemplateOutboundAsync(recipient, "Test", $"Test template SSA {DateTime.Now:dd/MM HH:mm:ss}");
                });

                if (result.Success) ToastHelper.ShowSuccess("WhatsApp", result.Message);
                else ToastHelper.ShowError("WhatsApp", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("WhatsApp", ex.Message);
            }
            finally
            {
                BtnTestWhatsAppTemplate.IsEnabled = true;
            }
        }

        private async void BtnTestBaileys_Click(object sender, RoutedEventArgs e)
        {
            BtnTestBaileys.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var service = new BaileysSidecarService(_configService, _loggingService);
                    return await service.TestHealthAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("Baileys", result.Message);
                else ToastHelper.ShowError("Baileys", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Baileys", ex.Message);
            }
            finally
            {
                BtnTestBaileys.IsEnabled = true;
            }
        }

        private async void BtnStartBaileysPairing_Click(object sender, RoutedEventArgs e)
        {
            BtnStartBaileysPairing.IsEnabled = false;
            try
            {
                string? phoneNumber = AutomationEngine.NormalizeWhatsAppNumber(TxtBaileysBotPhoneNumber.Text);
                if (string.IsNullOrWhiteSpace(phoneNumber))
                {
                    ToastHelper.ShowError("Baileys Pairing", "Nomor bot WhatsApp belum valid.");
                    return;
                }

                bool paired = await RunWithDraftConfigAsync(() =>
                {
                    var pairingWindow = new BaileysPairingWindow(_configService, _loggingService, phoneNumber)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    pairingWindow.ShowDialog();
                    return Task.FromResult(pairingWindow.PairingSucceeded);
                });

                if (paired)
                {
                    ToastHelper.ShowSuccess("Baileys Pairing", "WhatsApp berhasil terhubung.");
                }

                ValidateForm();
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Baileys Pairing", ex.Message);
            }
            finally
            {
                BtnStartBaileysPairing.IsEnabled = true;
            }
        }

        private async void BtnTestTunnel_Click(object sender, RoutedEventArgs e)
        {
            BtnTestTunnel.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var tunnel = new TunnelManager(_configService, _loggingService);
                    int port = int.TryParse(TxtWhatsAppLocalPort.Text, out var parsedPort) ? parsedPort : 8090;
                    return await tunnel.TestReachabilityAsync(port);
                });

                if (result.Success) ToastHelper.ShowSuccess("Tunnel", result.Message);
                else ToastHelper.ShowError("Tunnel", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Tunnel", ex.Message);
            }
            finally
            {
                BtnTestTunnel.IsEnabled = true;
            }
        }

        private async void BtnTestSheets_Click(object sender, RoutedEventArgs e)
        {
            BtnTestSheets.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var service = new GoogleSheetsService(_configService, _loggingService);
                    return await service.TestConnectionAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("Google Sheets", result.Message);
                else ToastHelper.ShowError("Google Sheets", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Google Sheets", ex.Message);
            }
            finally
            {
                BtnTestSheets.IsEnabled = ChkSheetsEnabled.IsChecked == true;
            }
        }

        private async void BtnPrepareSheets_Click(object sender, RoutedEventArgs e)
        {
            BtnPrepareSheets.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    var sheetsService = new GoogleSheetsService(_configService, _loggingService);
                    var syncService = new GoogleSheetsSyncService(_configService, _loggingService, sheetsService, _posDbService);
                    return await syncService.PreparePrioritySheetsAsync();
                });

                if (result.Success) ToastHelper.ShowSuccess("Google Sheets", result.Message);
                else ToastHelper.ShowError("Google Sheets", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Google Sheets", ex.Message);
            }
            finally
            {
                BtnPrepareSheets.IsEnabled = ChkSheetsEnabled.IsChecked == true;
            }
        }

        private async void BtnSyncSheetsDaily_Click(object sender, RoutedEventArgs e)
        {
            BtnSyncSheetsDaily.IsEnabled = false;
            try
            {
                var result = await RunWithDraftConfigAsync(async () =>
                {
                    if (_posDbService == null)
                    {
                        return (Success: false, Message: "Database pos.db belum siap.");
                    }

                    var sheetsService = new GoogleSheetsService(_configService, _loggingService);
                    var syncService = new GoogleSheetsSyncService(_configService, _loggingService, sheetsService, _posDbService);
                    return await syncService.SyncDailySnapshotAsync(DateTime.Today);
                });

                if (result.Success) ToastHelper.ShowSuccess("Google Sheets", result.Message);
                else ToastHelper.ShowError("Google Sheets", result.Message);
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Google Sheets", ex.Message);
            }
            finally
            {
                BtnSyncSheetsDaily.IsEnabled = ChkSheetsEnabled.IsChecked == true && _posDbService != null;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppConfig draft = BuildDraftConfig();
                if (!string.IsNullOrWhiteSpace(draft.Telegram?.BotToken) &&
                    draft.Telegram.BotToken != "YOUR_TELEGRAM_BOT_TOKEN")
                {
                    string? telegramError = TelegramBotService.ValidateBotToken(draft.Telegram.BotToken);
                    if (!string.IsNullOrWhiteSpace(telegramError))
                    {
                        ToastHelper.ShowError("Save Failed", telegramError);
                        return;
                    }
                }

                _configService.ReplaceInMemoryConfig(draft, save: true);
                _isDirty = false;
                ValidateForm();
                UpdateDraftStatus();

                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.RefreshConfiguredServices();
                }

                ToastHelper.ShowSuccess("Settings", "Configuration saved successfully.");
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Save Failed", ex.Message);
            }
        }

        private async void BtnForceDailySync_Click(object sender, RoutedEventArgs e)
        {
            BtnForceDailySync.IsEnabled = false;
            try
            {
                AppConfig draft = BuildDraftConfig();
                _configService.ReplaceInMemoryConfig(draft, save: true);

                var engine = CreateAutomationEngine();
                string result = await engine.ForceDualStockDailySyncAsync(DateTime.Now, "manual settings button");
                ToastHelper.ShowSuccess("Dual Stok", result, Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                ToastHelper.ShowError("Dual Stok", ex.Message, Window.GetWindow(this));
            }
            finally
            {
                BtnForceDailySync.IsEnabled = true;
            }
        }

        private void BtnTestConnections_Click(object sender, RoutedEventArgs e)
        {
            AppConfig draft = BuildDraftConfig();
            string mode = WhatsAppModes.Normalize(draft.WhatsApp?.Mode);
            bool waCloudReady = !WhatsAppModes.UsesCloudApi(mode) || (
                !string.IsNullOrWhiteSpace(draft.WhatsApp?.AccessToken) &&
                !string.IsNullOrWhiteSpace(draft.WhatsApp?.PhoneNumberId));
            bool baileysReady = !WhatsAppModes.UsesBaileys(mode) || (
                draft.Baileys?.Enabled == true &&
                !string.IsNullOrWhiteSpace(draft.Baileys.BotPhoneNumber) &&
                !string.IsNullOrWhiteSpace(draft.Baileys.NodeBinaryPath) &&
                !string.IsNullOrWhiteSpace(draft.Baileys.SidecarEntryPath));

            string result =
                "Connection draft summary\n\n" +
                $"Groq: {(draft.Groq?.ApiKey != null && draft.Groq.ApiKey != "YOUR_GROQ_API_KEY" ? "Configured" : "Not configured")}\n" +
                $"Gemini: {(ChkEnableFallback.IsChecked == true ? (!string.IsNullOrWhiteSpace(draft.Groq?.FallbackApiKey) ? "Configured" : "Missing key") : "Disabled")}\n" +
                $"Telegram: {(draft.Telegram?.BotToken != null && draft.Telegram.BotToken != "YOUR_TELEGRAM_BOT_TOKEN" ? "Configured" : "Not configured")}\n" +
                $"WhatsApp Mode: {mode}\n" +
                $"WhatsApp Cloud API: {(waCloudReady ? "Ready" : "Missing fields")}\n" +
                $"Baileys: {(baileysReady ? "Ready" : "Missing fields")}\n" +
                $"Tunnel: {(draft.Tunnel?.Enabled == true ? $"Enabled ({draft.Tunnel.Provider})" : "Disabled")}\n" +
                $"Database: {(draft.PosDb?.AutoDetect == true ? "Auto-detect" : string.IsNullOrWhiteSpace(draft.PosDb?.DatabasePath) ? "Missing path" : "Manual path configured")}";

            ToastHelper.ShowInfo("Connections", result, Window.GetWindow(this));
        }

        private AutomationEngine CreateAutomationEngine()
        {
            var groqService = new GroqService(_configService, _loggingService);
            return new AutomationEngine(_configService, groqService, new DatabaseService(), _loggingService, _posDbService);
        }

        private WhatsAppHandler CreateWhatsAppHandler()
        {
            return new WhatsAppHandler(_configService, _loggingService, CreateAutomationEngine());
        }
    }
}
