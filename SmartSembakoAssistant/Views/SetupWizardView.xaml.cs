using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class SetupWizardView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly SetupReadinessService _setupReadinessService;
        private CancellationTokenSource? _pairingCts;

        public event EventHandler? SetupCompleted;
        public event EventHandler? OpenAdvancedRequested;

        public SetupWizardView(
            ConfigService configService,
            LoggingService loggingService)
        {
            InitializeComponent();
            _configService = configService;
            _loggingService = loggingService;
            _setupReadinessService = new SetupReadinessService(_configService);

            LoadDefaults();
        }

        private void LoadDefaults()
        {
            _setupReadinessService.SeedDefaults();
            var config = _configService.Config ?? new Models.AppConfig();

            SelectMode(config.Setup?.PreferredChannelMode ?? "WhatsAppOnly");
            TxtBotPhoneNumber.Text = CleanPlaceholderPhone(config.Baileys?.BotPhoneNumber);
            TxtOwnerPhoneNumber.Text = CleanPlaceholderPhone(config.Baileys?.OwnerNumbers?.FirstOrDefault() ?? config.WhatsApp?.OwnerNumbers?.FirstOrDefault());
            TxtGroqApiKey.Text = config.Groq?.ApiKey == "YOUR_GROQ_API_KEY" ? "" : config.Groq?.ApiKey ?? "";
            TxtTelegramToken.Text = config.Telegram?.BotToken == "YOUR_TELEGRAM_BOT_TOKEN" ? "" : config.Telegram?.BotToken ?? "";

            TxtWizardSummary.Text = string.IsNullOrWhiteSpace(config.PosDb?.DatabasePath)
                ? "Database pos.db akan dicoba dideteksi otomatis saat setup."
                : $"Database terdeteksi: {config.PosDb?.DatabasePath}";
            TxtBasicStatus.Text = "Mode default memakai WhatsApp lokal via Baileys. Settings teknis dipindah ke menu Settings.";
            UpdateModeHints();
        }

        private async void BtnSaveAndConnect_Click(object sender, RoutedEventArgs e)
        {
            BtnSaveAndConnect.IsEnabled = false;
            try
            {
                string channelMode = GetSelectedMode();
                if (string.IsNullOrWhiteSpace(TxtOwnerPhoneNumber.Text))
                {
                    TxtBasicStatus.Text = "Nomor owner/admin wajib diisi.";
                    return;
                }

                if (channelMode != "TelegramOnly" && string.IsNullOrWhiteSpace(TxtBotPhoneNumber.Text))
                {
                    TxtBasicStatus.Text = "Nomor bot WhatsApp wajib diisi untuk mode WhatsApp lokal.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtGroqApiKey.Text))
                {
                    TxtBasicStatus.Text = "Groq API key wajib diisi.";
                    return;
                }

                if (channelMode != "WhatsAppOnly" && string.IsNullOrWhiteSpace(TxtTelegramToken.Text))
                {
                    TxtBasicStatus.Text = "Telegram bot token wajib diisi jika Telegram dipakai.";
                    return;
                }

                if (channelMode != "WhatsAppOnly")
                {
                    string? telegramError = TelegramBotService.ValidateBotToken(TxtTelegramToken.Text);
                    if (!string.IsNullOrWhiteSpace(telegramError))
                    {
                        TxtBasicStatus.Text = telegramError;
                        return;
                    }
                }

                _setupReadinessService.ApplyBasicSetup(
                    channelMode,
                    TxtOwnerPhoneNumber.Text,
                    TxtGroqApiKey.Text,
                    TxtBotPhoneNumber.Text,
                    TxtTelegramToken.Text);

                TxtBasicStatus.Text = "Konfigurasi dasar tersimpan. Menyiapkan runtime...";

                if (channelMode == "TelegramOnly")
                {
                    FinishSetup("Setup selesai. Anda bisa langsung menjalankan Telegram bot.");
                    return;
                }

                PairingCard.Visibility = Visibility.Visible;
                await BeginPairingFlowAsync(generateNewCode: true, resetSession: false);
            }
            catch (Exception ex)
            {
                TxtBasicStatus.Text = ex.Message;
                ToastHelper.ShowError("Setup", ex.Message, Window.GetWindow(this));
            }
            finally
            {
                BtnSaveAndConnect.IsEnabled = true;
            }
        }

        private async Task BeginPairingFlowAsync(bool generateNewCode, bool resetSession)
        {
            await Task.Yield();
            _pairingCts?.Cancel();
            TxtPairingStatus.Text = "Membuka jendela pairing Baileys...";
            TxtPairingDiagnostics.Text = resetSession
                ? "Reset sesi sekarang dilakukan dari jendela pairing dengan konfirmasi."
                : "Kode pairing akan tampil di jendela khusus agar tidak hilang seperti toast.";

            var pairingWindow = new BaileysPairingWindow(_configService, _loggingService, TxtBotPhoneNumber.Text)
            {
                Owner = Window.GetWindow(this)
            };
            pairingWindow.ShowDialog();

            if (pairingWindow.PairingSucceeded)
            {
                FinishSetup("WhatsApp berhasil dipairing dan siap dipakai.");
                return;
            }

            TxtPairingStatus.Text = "Pairing belum selesai.";
            TxtPairingDiagnostics.Text = "Buka lagi jendela pairing jika ingin melanjutkan. Jangan generate kode baru terlalu sering.";
        }

        private void StartPairingPoll()
        {
            _pairingCts?.Cancel();
            _pairingCts = new CancellationTokenSource();
            var token = _pairingCts.Token;

            _ = Task.Run(async () =>
            {
                var service = new BaileysSidecarService(_configService, _loggingService);
                int attempt = 0;
                while (!token.IsCancellationRequested && attempt < 60)
                {
                    attempt++;
                    await Task.Delay(3000, token);
                    var status = await service.GetSessionStatusAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (status == null)
                        {
                            TxtPairingDiagnostics.Text = service.LastError ?? "Status pairing belum tersedia.";
                            return;
                        }

                        TxtPairingCode.Text = string.IsNullOrWhiteSpace(status.PairingCode) ? TxtPairingCode.Text : status.PairingCode;
                        TxtPairingDiagnostics.Text = !string.IsNullOrWhiteSpace(status.Error)
                            ? status.Error
                            : !string.IsNullOrWhiteSpace(status.LastDisconnectReason)
                                ? $"Disconnect {status.LastDisconnectStatusCode?.ToString() ?? "-"}: {status.LastDisconnectReason}"
                                : $"Menunggu pairing... percobaan {attempt}/60";
                        if (status.Paired && status.Connected)
                        {
                            TxtPairingStatus.Text = "WhatsApp berhasil terhubung.";
                            FinishSetup("WhatsApp berhasil dipairing dan siap dipakai.");
                        }
                    });

                    if (status?.Paired == true && status.Connected)
                    {
                        return;
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    TxtPairingStatus.Text = "Masih menunggu pairing.";
                    if (string.IsNullOrWhiteSpace(TxtPairingDiagnostics.Text))
                    {
                        TxtPairingDiagnostics.Text = "Jika kode belum dimasukkan di WhatsApp, tunggu cooldown lalu gunakan Generate Kode Baru satu kali.";
                    }
                });
            }, token);
        }

        private void FinishSetup(string message)
        {
            _pairingCts?.Cancel();
            TxtBasicStatus.Text = message;
            TxtPairingStatus.Text = message;
            ToastHelper.ShowSuccess("Setup", message, Window.GetWindow(this));
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void BtnCopyPairingCode_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPairingCode.Text) || TxtPairingCode.Text == "-")
            {
                ToastHelper.ShowWarning("Pairing", "Kode pairing belum tersedia.", Window.GetWindow(this));
                return;
            }

            Clipboard.SetText(TxtPairingCode.Text.Trim());
            ToastHelper.ShowSuccess("Pairing", "Kode pairing berhasil disalin.", Window.GetWindow(this));
        }

        private async void BtnRetryPairing_Click(object sender, RoutedEventArgs e)
        {
            await BeginPairingFlowAsync(generateNewCode: true, resetSession: false);
        }

        private async void BtnResetSession_Click(object sender, RoutedEventArgs e)
        {
            await BeginPairingFlowAsync(generateNewCode: true, resetSession: true);
        }

        private void BtnFinishWithoutPairing_Click(object sender, RoutedEventArgs e)
        {
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void BtnOpenAdvanced_Click(object sender, RoutedEventArgs e)
        {
            OpenAdvancedRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CmbChannelMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateModeHints();
        }

        private void UpdateModeHints()
        {
            string mode = GetSelectedMode();
            PanelTelegram.Visibility = mode == "TelegramOnly" || mode == "TelegramAndWhatsApp"
                ? Visibility.Visible
                : Visibility.Collapsed;
            TxtBotPhoneNumber.IsEnabled = mode != "TelegramOnly";
            TxtBotPhoneHint.Text = mode == "TelegramOnly"
                ? "Nomor bot WhatsApp tidak diperlukan jika Anda hanya memakai Telegram."
                : "Nomor ini akan dipakai untuk generate pairing code WhatsApp lokal.";
        }

        private void SelectMode(string mode)
        {
            foreach (ComboBoxItem item in CmbChannelMode.Items)
            {
                if (ToChannelMode(item.Content?.ToString()) == mode)
                {
                    CmbChannelMode.SelectedItem = item;
                    return;
                }
            }

            CmbChannelMode.SelectedIndex = 0;
        }

        private string GetSelectedMode()
        {
            var selected = CmbChannelMode.SelectedItem as ComboBoxItem;
            return ToChannelMode(selected?.Content?.ToString());
        }

        private static string ToChannelMode(string? displayText)
        {
            return displayText switch
            {
                "Telegram + WhatsApp" => "TelegramAndWhatsApp",
                "Telegram saja" => "TelegramOnly",
                _ => "WhatsAppOnly"
            };
        }

        private static string CleanPlaceholderPhone(string? value)
        {
            string normalized = AutomationEngine.NormalizeWhatsAppNumber(value);
            return normalized == "6281234567890" || normalized == "6280000000000" ? string.Empty : value ?? string.Empty;
        }
    }
}
