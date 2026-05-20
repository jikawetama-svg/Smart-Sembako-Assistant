using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class BaileysPairingWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly string? _phoneNumber;
        private readonly DispatcherTimer _countdownTimer;
        private CancellationTokenSource? _pollCts;
        private BaileysSidecarService? _service;
        private DateTime? _codeExpiresAt;
        private DateTime? _qrExpiresAt;
        private DateTime? _cooldownUntil;
        private bool _isBusy;
        private bool _isConnected;
        private bool _isLoggedOut;
        private bool _loaded;
        private string? _rawPairingCode;

        public bool PairingSucceeded { get; private set; }

        public BaileysPairingWindow(
            ConfigService configService,
            LoggingService loggingService,
            string? phoneNumber)
        {
            InitializeComponent();
            _configService = configService;
            _loggingService = loggingService;
            _phoneNumber = phoneNumber;
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += (_, _) => RefreshCountdownUi();
            TxtBotPhoneHint.Text = $"Nomor bot yang dipairing: {FormatPhoneNumber(_phoneNumber)}. Buka WhatsApp pada nomor ini, bukan nomor owner/kasir.";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _countdownTimer.Start();
            await GenerateCodeAsync();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _pollCts?.Cancel();
            _countdownTimer.Stop();
        }

        private async Task GenerateCodeAsync()
        {
            if (_isBusy || _isConnected)
            {
                return;
            }

            if (_isLoggedOut)
            {
                TxtPairingStatus.Text = "Sesi WhatsApp lokal logged out.";
                TxtPairingDiagnostics.Text = "Reset sesi lokal dulu, tunggu cooldown singkat, lalu generate satu kode baru.";
                RefreshCountdownUi();
                return;
            }

            DateTime now = DateTime.Now;
            DateTime? blockedUntil = GetBlockedUntil(now);
            if (blockedUntil.HasValue && blockedUntil.Value > now)
            {
                TxtPairingStatus.Text = "Kode pairing atau cooldown masih aktif.";
                TxtPairingDiagnostics.Text = $"Tunggu {FormatRemaining(blockedUntil.Value - now)} sebelum generate kode baru.";
                RefreshCountdownUi();
                return;
            }

            SetBusy(true);
            try
            {
                _service = new BaileysSidecarService(_configService, _loggingService);
                TxtPairingStatus.Text = "Menyiapkan WhatsApp lokal...";
                TxtPairingDiagnostics.Text = "";

                var health = await _service.TestHealthAsync();
                TxtPairingDiagnostics.Text = health.Message;
                var status = await _service.GetSessionStatusAsync();
                if (IsLoggedOutStatus(status, _service))
                {
                    SetLoggedOutState(status?.Error ?? _service.LastError ?? health.Message);
                    return;
                }

                if (!health.Success && !_service.IsReachable)
                {
                    TxtPairingStatus.Text = "WhatsApp lokal belum siap.";
                    ApplyCooldown(_configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
                    return;
                }

                if (_service.IsPaired)
                {
                    MarkConnected("WhatsApp sudah terhubung.");
                    return;
                }

                var result = await _service.GeneratePairingCodeAsync(_phoneNumber);
                TxtPairingStatus.Text = result.Success
                    ? "Masukkan kode pairing di WhatsApp pada nomor bot."
                    : "Kode pairing gagal dibuat.";
                TxtPairingDiagnostics.Text = result.Message;

                if (result.Success && !string.IsNullOrWhiteSpace(result.PairingCode))
                {
                    SetPairingCode(result.PairingCode);
                    _codeExpiresAt = result.ExpiresAt ?? DateTime.Now.AddSeconds(_configService.Config?.Baileys?.PairingCodeTtlSeconds ?? 120);
                    ApplyCooldown(result.RetryAfterSeconds);
                    StartPairingPoll();
                    return;
                }

                if (result.Success && _service.IsPaired)
                {
                    MarkConnected(result.Message);
                    return;
                }

                ApplyCooldown(result.RetryAfterSeconds ?? _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
                if (string.Equals(result.Reason, "rate-limited", StringComparison.OrdinalIgnoreCase))
                {
                    TxtPairingStatus.Text = "Permintaan pairing terlalu sering.";
                }
            }
            catch (Exception ex)
            {
                TxtPairingStatus.Text = "Pairing gagal.";
                TxtPairingDiagnostics.Text = ex.Message;
                ApplyCooldown(_configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void StartPairingPoll()
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var service = new BaileysSidecarService(_configService, _loggingService);
                    int attempt = 0;
                    while (!token.IsCancellationRequested)
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

                            if (!string.IsNullOrWhiteSpace(status.PairingCode))
                            {
                                SetPairingCode(status.PairingCode);
                            }

                            if (status.PairingCodeExpiresAt.HasValue)
                            {
                                _codeExpiresAt = status.PairingCodeExpiresAt.Value.ToLocalTime();
                            }

                            if (!string.IsNullOrWhiteSpace(status.QrDataUrl))
                            {
                                SetQrCode(status.QrDataUrl);
                            }

                            if (status.QrCodeExpiresAt.HasValue)
                            {
                                _qrExpiresAt = status.QrCodeExpiresAt.Value.ToLocalTime();
                            }

                            ApplyCooldown(status.RetryAfterSeconds);

                            if (IsLoggedOutStatus(status, service))
                            {
                                SetLoggedOutState(status.Error ?? service.LastError ?? "Sesi WhatsApp lokal logged out.");
                                return;
                            }

                            TxtPairingDiagnostics.Text = !string.IsNullOrWhiteSpace(status.Error)
                                ? status.Error
                                : !string.IsNullOrWhiteSpace(status.LastDisconnectReason)
                                    ? $"Disconnect {status.LastDisconnectStatusCode?.ToString() ?? "-"}: {status.LastDisconnectReason}"
                                    : $"Menunggu pairing... percobaan {attempt}";

                            if (status.Paired && status.Connected)
                            {
                                MarkConnected("WhatsApp berhasil terhubung. Nomor bot siap dipakai.");
                            }
                        });

                        if (status?.Paired == true && status.Connected)
                        {
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Window ditutup atau pairing selesai.
                }
            }, token);
        }

        private async void BtnGenerateCode_Click(object sender, RoutedEventArgs e)
        {
            await GenerateCodeAsync();
        }

        private async void BtnGenerateQr_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                "Fallback QR akan reset sesi WhatsApp lokal lalu membuat QR baru. Gunakan ini jika pairing code nomor telepon terus ditolak.",
                "Fallback QR Pairing",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            await StartQrPairingAsync();
        }

        private async Task StartQrPairingAsync()
        {
            if (_isBusy || _isConnected)
            {
                return;
            }

            SetBusy(true);
            try
            {
                _service ??= new BaileysSidecarService(_configService, _loggingService);
                TxtPairingStatus.Text = "Menyiapkan fallback QR...";
                TxtPairingDiagnostics.Text = "Reset sesi lokal dan menunggu QR dari Baileys.";
                _isLoggedOut = false;
                SetPairingCode(null);
                ClearQrCode();

                var result = await _service.StartQrPairingAsync(resetSessionFirst: true);
                TxtPairingStatus.Text = result.Success
                    ? "Scan QR dari WhatsApp nomor bot."
                    : "QR pairing gagal dibuat.";
                TxtPairingDiagnostics.Text = result.Message;

                if (result.Success && !string.IsNullOrWhiteSpace(result.QrDataUrl))
                {
                    SetQrCode(result.QrDataUrl);
                    _qrExpiresAt = result.ExpiresAt?.ToLocalTime() ?? DateTime.Now.AddSeconds(60);
                    _codeExpiresAt = null;
                    _cooldownUntil = null;
                    StartPairingPoll();
                    return;
                }

                if (result.Success && _service.IsPaired)
                {
                    MarkConnected(result.Message);
                    return;
                }

                ApplyCooldown(result.RetryAfterSeconds ?? _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
            }
            catch (Exception ex)
            {
                TxtPairingStatus.Text = "QR pairing gagal.";
                TxtPairingDiagnostics.Text = ex.Message;
                ApplyCooldown(_configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPairingCode.Text) || TxtPairingCode.Text == "-")
            {
                ToastHelper.ShowWarning("Pairing", "Kode pairing belum tersedia.", this);
                return;
            }

            Clipboard.SetText((_rawPairingCode ?? TxtPairingCode.Text).Replace("-", "").Trim());
            ToastHelper.ShowSuccess("Pairing", "Kode pairing berhasil disalin.", this);
        }

        private async void BtnResetSession_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                "Reset akan menghapus sesi WhatsApp lokal. Gunakan hanya jika pairing gagal berulang atau status menunjukkan logged out. Setelah reset, tunggu cooldown sebelum generate kode baru.",
                "Reset Sesi Lokal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusy(true);
            try
            {
                _service ??= new BaileysSidecarService(_configService, _loggingService);
                var result = await _service.ResetSessionAsync();
                TxtPairingStatus.Text = result.Success ? "Sesi lokal direset." : "Reset sesi gagal.";
                TxtPairingDiagnostics.Text = result.Message;
                _isLoggedOut = false;
                SetPairingCode(null);
                ClearQrCode();
                _codeExpiresAt = null;
                _qrExpiresAt = null;
                ApplyCooldown(result.RetryAfterSeconds ?? _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
            }
            catch (Exception ex)
            {
                TxtPairingStatus.Text = "Reset sesi gagal.";
                TxtPairingDiagnostics.Text = ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MarkConnected(string message)
        {
            _isConnected = true;
            PairingSucceeded = true;
            _pollCts?.Cancel();
            TxtPairingStatus.Text = message;
            TxtPairingDiagnostics.Text = "Pairing selesai. Anda bisa menutup jendela ini.";
            SetPairingCode(null);
            ClearQrCode();
            _codeExpiresAt = null;
            _qrExpiresAt = null;
            _cooldownUntil = null;
            RefreshCountdownUi();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            RefreshCountdownUi();
        }

        private void ApplyCooldown(int? retryAfterSeconds)
        {
            int seconds = retryAfterSeconds.GetValueOrDefault();
            if (seconds <= 0)
            {
                return;
            }

            DateTime next = DateTime.Now.AddSeconds(seconds);
            if (!_cooldownUntil.HasValue || next > _cooldownUntil.Value)
            {
                _cooldownUntil = next;
            }
        }

        private DateTime? GetBlockedUntil(DateTime now)
        {
            var candidates = new[] { _codeExpiresAt, _cooldownUntil }
                .Where(value => value.HasValue && value.Value > now)
                .Select(value => value!.Value)
                .ToList();

            return candidates.Count == 0 ? null : candidates.Max();
        }

        private void RefreshCountdownUi()
        {
            DateTime now = DateTime.Now;
            if (_isConnected)
            {
                TxtCodeCountdown.Text = "WhatsApp berhasil terhubung.";
            }
            else if (_isLoggedOut)
            {
                TxtCodeCountdown.Text = "Sesi logged out. Reset sesi lokal dulu sebelum generate kode.";
            }
            else if (_codeExpiresAt.HasValue && _codeExpiresAt.Value > now)
            {
                TxtCodeCountdown.Text = $"Kode aktif sekitar {FormatRemaining(_codeExpiresAt.Value - now)}.";
            }
            else if (_qrExpiresAt.HasValue && _qrExpiresAt.Value > now)
            {
                TxtCodeCountdown.Text = $"QR aktif sekitar {FormatRemaining(_qrExpiresAt.Value - now)}.";
            }
            else if (_cooldownUntil.HasValue && _cooldownUntil.Value > now)
            {
                TxtCodeCountdown.Text = $"Cooldown generate kode baru: {FormatRemaining(_cooldownUntil.Value - now)}.";
            }
            else if (TxtPairingCode.Text != "-")
            {
                TxtCodeCountdown.Text = "Kode kemungkinan sudah expired. Generate kode baru satu kali jika perlu.";
            }
            else
            {
                TxtCodeCountdown.Text = "Belum ada kode aktif.";
            }

            DateTime? blockedUntil = GetBlockedUntil(now);
            bool blocked = blockedUntil.HasValue && blockedUntil.Value > now;
            bool cooldownBlocked = _cooldownUntil.HasValue && _cooldownUntil.Value > now;
            BtnGenerateCode.IsEnabled = !_isBusy && !_isConnected && !blocked && !_isLoggedOut;
            BtnGenerateCode.Content = _isLoggedOut
                ? "Reset Sesi Lokal dulu"
                : blocked
                ? $"Generate Kode Baru dalam {FormatRemaining(blockedUntil!.Value - now)}"
                : "Generate Kode Baru";
            BtnResetSession.IsEnabled = !_isBusy && !_isConnected && (!cooldownBlocked || _isLoggedOut);
            BtnGenerateQr.IsEnabled = !_isBusy && !_isConnected;
            BtnCopyCode.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(_rawPairingCode);
            BtnClose.IsEnabled = !_isBusy || _isConnected;

            if (_qrExpiresAt.HasValue && _qrExpiresAt.Value > now)
            {
                TxtQrCountdown.Text = $"QR aktif sekitar {FormatRemaining(_qrExpiresAt.Value - now)}.";
            }
            else if (BorderQrPairing.Visibility == Visibility.Visible)
            {
                TxtQrCountdown.Text = "QR kemungkinan sudah expired. Klik Fallback QR untuk membuat QR baru.";
            }
        }

        private void TxtManualCodeCheck_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateManualCode();
        }

        private void SetPairingCode(string? code)
        {
            _rawPairingCode = NormalizeCode(code);
            string formatted = FormatPairingCode(_rawPairingCode);
            TxtPairingCode.Text = formatted;
            SetCodeTileText(_rawPairingCode);
            TxtCharacterHint.Text = BuildCharacterHint(_rawPairingCode);
            TxtManualCodeCheck.Text = "";
            ValidateManualCode();
            RefreshCountdownUi();
        }

        private void ValidateManualCode()
        {
            if (TxtManualCodeValidation == null)
            {
                return;
            }

            string typed = NormalizeCode(TxtManualCodeCheck?.Text);
            if (string.IsNullOrWhiteSpace(_rawPairingCode))
            {
                TxtManualCodeValidation.Text = "Kode pairing belum tersedia.";
                TxtManualCodeValidation.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                return;
            }

            if (string.IsNullOrWhiteSpace(typed))
            {
                TxtManualCodeValidation.Text = "Ketik ulang kode dari layar HP jika ingin memastikan tidak ada karakter yang tertukar.";
                TxtManualCodeValidation.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                return;
            }

            if (string.Equals(typed, _rawPairingCode, StringComparison.OrdinalIgnoreCase))
            {
                TxtManualCodeValidation.Text = "Kode cocok. Jangan generate ulang; tunggu WhatsApp menyelesaikan pairing.";
                TxtManualCodeValidation.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                return;
            }

            int mismatchIndex = FindFirstMismatch(typed, _rawPairingCode);
            string expected = mismatchIndex >= 0 && mismatchIndex < _rawPairingCode.Length
                ? _rawPairingCode[mismatchIndex].ToString()
                : "-";
            string actual = mismatchIndex >= 0 && mismatchIndex < typed.Length
                ? typed[mismatchIndex].ToString()
                : "-";
            TxtManualCodeValidation.Text = $"Kode berbeda di karakter ke-{mismatchIndex + 1}: tertulis {actual}, seharusnya {expected}.";
            TxtManualCodeValidation.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        }

        private void SetLoggedOutState(string message)
        {
            _isLoggedOut = true;
            _pollCts?.Cancel();
            SetPairingCode(null);
            ClearQrCode();
            _codeExpiresAt = null;
            _qrExpiresAt = null;
            TxtPairingStatus.Text = "Sesi WhatsApp lokal logged out.";
            TxtPairingDiagnostics.Text = $"{message} Reset sesi lokal dulu, tunggu cooldown singkat, lalu generate satu kode baru.";
            RefreshCountdownUi();
        }

        private static bool IsLoggedOutStatus(BaileysSessionStatus? status, BaileysSidecarService service)
        {
            return status?.LastDisconnectStatusCode == 401 ||
                   service.LastDisconnectStatusCode == 401 ||
                   ContainsLoggedOut(status?.Error) ||
                   ContainsLoggedOut(service.LastError);
        }

        private static bool ContainsLoggedOut(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Contains("logged out", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCode(string? code)
        {
            return string.IsNullOrWhiteSpace(code)
                ? ""
                : new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }

        private static string FormatPairingCode(string? code)
        {
            string normalized = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "-";
            }

            return normalized.Length > 4
                ? $"{normalized[..4]}-{normalized[4..]}"
                : normalized;
        }

        private void SetCodeTileText(string? code)
        {
            string normalized = NormalizeCode(code).PadRight(8, '-');
            TextBlock[] tiles =
            {
                TxtCodeChar1, TxtCodeChar2, TxtCodeChar3, TxtCodeChar4,
                TxtCodeChar5, TxtCodeChar6, TxtCodeChar7, TxtCodeChar8
            };

            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].Text = normalized[i].ToString();
            }
        }

        private void SetQrCode(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                ClearQrCode();
                return;
            }

            try
            {
                string base64 = dataUrl;
                int commaIndex = dataUrl.IndexOf(',');
                if (commaIndex >= 0)
                {
                    base64 = dataUrl[(commaIndex + 1)..];
                }

                byte[] bytes = Convert.FromBase64String(base64);
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                ImgQrCode.Source = image;
                BorderQrPairing.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TxtPairingDiagnostics.Text = $"QR diterima tapi gagal ditampilkan: {ex.Message}";
                ClearQrCode();
            }
        }

        private void ClearQrCode()
        {
            ImgQrCode.Source = null;
            BorderQrPairing.Visibility = Visibility.Collapsed;
            TxtQrCountdown.Text = "";
            _qrExpiresAt = null;
        }

        private static string BuildCharacterHint(string? code)
        {
            string normalized = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            var hints = normalized
                .Select((character, index) => BuildSingleCharacterHint(character, index + 1))
                .Where(hint => !string.IsNullOrWhiteSpace(hint))
                .ToList();

            return hints.Count == 0
                ? "Pastikan kode dimasukkan pada nomor bot yang sama."
                : string.Join("  |  ", hints);
        }

        private static string? BuildSingleCharacterHint(char character, int index)
        {
            return character switch
            {
                '4' => $"Karakter {index}: angka 4, bukan huruf A",
                'A' => $"Karakter {index}: huruf A, bukan angka 4",
                '1' => $"Karakter {index}: angka 1, bukan huruf I",
                'I' => $"Karakter {index}: huruf I, bukan angka 1",
                '0' => $"Karakter {index}: angka 0, bukan huruf O",
                'O' => $"Karakter {index}: huruf O, bukan angka 0",
                '5' => $"Karakter {index}: angka 5, bukan huruf S",
                'S' => $"Karakter {index}: huruf S, bukan angka 5",
                _ => null
            };
        }

        private static int FindFirstMismatch(string actual, string expected)
        {
            int max = Math.Max(actual.Length, expected.Length);
            for (int i = 0; i < max; i++)
            {
                char actualChar = i < actual.Length ? actual[i] : '\0';
                char expectedChar = i < expected.Length ? expected[i] : '\0';
                if (actualChar != expectedChar)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string FormatPhoneNumber(string? phoneNumber)
        {
            string normalized = new string((phoneNumber ?? "").Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "-";
            }

            return normalized.StartsWith("62", StringComparison.Ordinal)
                ? $"+{normalized}"
                : normalized;
        }

        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            return remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }
}
