using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using SmartSembakoAssistant.Models;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace SmartSembakoAssistant.Services
{
    public class TelegramBotService
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly AutomationEngine _automationEngine;
        private TelegramBotClient? _botClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;
        private DateTime? _lastPollingConflictLoggedAt;
        private static readonly HttpClient _cloudNotifyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        public bool IsRunning { get; private set; }
        public string? LastError { get; private set; }
        public DateTime? LastValidatedAt { get; private set; }

        public TelegramBotService(
            ConfigService configService,
            LoggingService loggingService,
            AutomationEngine automationEngine)
        {
            _configService = configService;
            _loggingService = loggingService;
            _automationEngine = automationEngine;
        }

        public async Task<bool> StartAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (IsRunning)
                {
                    return true;
                }

                LastError = null;
                string? botToken = _configService.Config?.Telegram?.BotToken;
                string? tokenValidationError = ValidateBotToken(botToken);
                if (!string.IsNullOrWhiteSpace(tokenValidationError))
                {
                    LastError = $"{tokenValidationError} Config aktif: {_configService.ConfigPath}";
                    await _loggingService.LogWarningAsync(LastError, "Telegram");
                    return false;
                }

                string normalizedBotToken = botToken!.Trim();
                if (!TryAcquireSingleInstanceMutex(normalizedBotToken))
                {
                    IsRunning = false;
                    LastError = "Telegram bot token sedang dipakai oleh instance Smart Sembako lain di PC ini. Tutup aplikasi/proses lain yang memakai token yang sama, lalu Start lagi.";
                    await _loggingService.LogWarningAsync(LastError, "Telegram");
                    return false;
                }

                _botClient = new TelegramBotClient(normalizedBotToken);
                _cancellationTokenSource = new CancellationTokenSource();

                var me = await _botClient.GetMeAsync(_cancellationTokenSource.Token);
                var webhookInfo = await _botClient.GetWebhookInfoAsync(_cancellationTokenSource.Token);
                if (!string.IsNullOrWhiteSpace(webhookInfo.Url))
                {
                    await _botClient.DeleteWebhookAsync(dropPendingUpdates: false, cancellationToken: _cancellationTokenSource.Token);
                    await _loggingService.LogInfoAsync(
                        $"Webhook Telegram lama dihapus agar long polling bisa aktif. Url sebelumnya: {webhookInfo.Url}",
                        "Telegram");
                }

                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    pollingErrorHandler: HandlePollingErrorAsync,
                    receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
                    cancellationToken: _cancellationTokenSource.Token);

                IsRunning = true;
                LastError = null;
                LastValidatedAt = DateTime.Now;
                await _loggingService.LogInfoAsync($"Telegram bot aktif: @{me.Username}", "Telegram");

                // Notifikasi ke Cloud Bot agar nonaktifkan webhook (Desktop takeover)
                _ = NotifyCloudBotAsync("desktop-online");

                return true;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 404)
            {
                IsRunning = false;
                LastValidatedAt = DateTime.Now;
                LastError = "Telegram token ditolak Bot API (Not Found). Gunakan token bot resmi dari BotFather dengan format angka:secret.";
                ReleaseSingleInstanceMutex();
                await _loggingService.LogErrorAsync(LastError, "Telegram", ex.ToString());
                return false;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 401)
            {
                IsRunning = false;
                LastValidatedAt = DateTime.Now;
                LastError = "Telegram token tidak valid atau sudah expired (Unauthorized).";
                ReleaseSingleInstanceMutex();
                await _loggingService.LogErrorAsync(LastError, "Telegram", ex.ToString());
                return false;
            }
            catch (TaskCanceledException ex)
            {
                IsRunning = false;
                LastValidatedAt = DateTime.Now;
                LastError = "Koneksi ke Telegram timeout. Periksa internet atau coba lagi.";
                ReleaseSingleInstanceMutex();
                await _loggingService.LogErrorAsync(LastError, "Telegram", ex.ToString());
                return false;
            }
            catch (HttpRequestException ex)
            {
                IsRunning = false;
                LastValidatedAt = DateTime.Now;
                LastError = "Koneksi ke Telegram gagal. Periksa internet atau firewall.";
                ReleaseSingleInstanceMutex();
                await _loggingService.LogErrorAsync(LastError, "Telegram", ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                LastValidatedAt = DateTime.Now;
                LastError = $"Gagal memulai Telegram bot: {ex.Message}";
                ReleaseSingleInstanceMutex();
                await _loggingService.LogErrorAsync(LastError, "Telegram", ex.ToString());
                return false;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _botClient = null;
                IsRunning = false;
                ReleaseSingleInstanceMutex();
                await _loggingService.LogInfoAsync("Telegram bot dihentikan.", "Telegram");

                // Notifikasi ke Cloud Bot agar aktifkan kembali webhook (failover)
                _ = NotifyCloudBotAsync("desktop-offline");
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// Kirim sinyal ke Cloud Bot (Render) untuk mengatur mode polling/webhook.
        /// desktop-online = Desktop aktif → Cloud Bot hapus webhook.
        /// desktop-offline = Desktop mati → Cloud Bot daftar webhook kembali.
        /// </summary>
        private async Task NotifyCloudBotAsync(string endpoint)
        {
            try
            {
                string? cloudBotUrl = _configService.Config?.App?.CloudBotUrl;
                string? secretToken = _configService.Config?.Telegram?.SecretToken
                    ?? "smart-sembako-secret-token";

                if (string.IsNullOrWhiteSpace(cloudBotUrl))
                    return; // Cloud Bot URL belum dikonfigurasi, skip

                string url = $"{cloudBotUrl.TrimEnd('/')}/internal/{endpoint}";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("X-Desktop-Secret", secretToken);
                req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                var resp = await _cloudNotifyClient.SendAsync(req);
                await _loggingService.LogInfoAsync(
                    $"Cloud Bot notified [{endpoint}]: HTTP {(int)resp.StatusCode}", "Failover");
            }
            catch (Exception ex)
            {
                // Non-critical: jika Cloud Bot tidak reachable, abaikan
                await _loggingService.LogWarningAsync(
                    $"Gagal notifikasi Cloud Bot [{endpoint}]: {ex.Message}", "Failover");
            }
        }

        public async Task SendMessageAsync(long chatId, string message, ParseMode? parseMode = null)
        {
            if (_botClient == null || !IsRunning)
            {
                return;
            }

            await _botClient.SendTextMessageAsync(chatId, message, parseMode: parseMode);
        }

        public async Task SendMessageAsync(string recipientId, string message)
        {
            if (long.TryParse(recipientId, out var chatId))
            {
                await SendMessageAsync(chatId, message);
            }
        }

        public async Task SendDocumentAsync(long chatId, string filePath, string caption = "")
        {
            if (_botClient == null || !IsRunning)
            {
                return;
            }

            using var stream = System.IO.File.OpenRead(filePath);
            var document = new InputFileStream(stream, System.IO.Path.GetFileName(filePath));
            await _botClient.SendDocumentAsync(chatId, document, caption: caption);
        }

        public async Task<string?> SendQueuedMessageAsync(OutboundMessageRecord message)
        {
            if (_botClient == null || !IsRunning || !long.TryParse(message.RecipientId, out var chatId))
            {
                return null;
            }

            var sentMessage = await _botClient.SendTextMessageAsync(
                chatId,
                message.Text,
                parseMode: ResolveParseMode(message.ParseMode),
                replyMarkup: BuildReplyMarkup(message, chatId));

            return sentMessage.MessageId.ToString();
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery?.Message != null)
                {
                    await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                    return;
                }

                if (update.Message == null)
                {
                    return;
                }

                if (!IsChatAllowed(update.Message.Chat.Id))
                {
                    await botClient.SendTextMessageAsync(
                        update.Message.Chat.Id,
                        "Akses ditolak. Chat ID Anda belum diizinkan di konfigurasi aplikasi.",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (update.Message.Type == MessageType.Photo)
                {
                    var photo = update.Message.Photo?.OrderByDescending(item => item.FileSize ?? 0).FirstOrDefault();
                    if (photo == null)
                    {
                        await botClient.SendTextMessageAsync(
                            update.Message.Chat.Id,
                            "Foto diterima, tetapi file gambar tidak bisa dibaca.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    string? localPhotoPath = await DownloadPhotoAsync(botClient, photo.FileId, cancellationToken);
                    var inboundPhoto = new Models.InboundMessage
                    {
                        Channel = Models.ChannelType.Telegram,
                        SenderId = update.Message.Chat.Id.ToString(),
                        SenderName = update.Message.From?.Username ?? update.Message.From?.FirstName ?? update.Message.Chat.Title,
                        Text = update.Message.Caption ?? string.Empty,
                        MediaUrl = localPhotoPath,
                        MessageId = update.Message.MessageId.ToString(),
                        Timestamp = update.Message.Date
                    };

                    await botClient.SendChatActionAsync(update.Message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
                    await _automationEngine.ProcessInboundMessageAsync(inboundPhoto);
                    return;
                }

                string text = update.Message.Text ?? update.Message.Caption ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendTextMessageAsync(
                        update.Message.Chat.Id,
                        BuildUnsupportedMessage(update.Message.Type),
                        cancellationToken: cancellationToken);
                    return;
                }

                var inbound = new Models.InboundMessage
                {
                    Channel = Models.ChannelType.Telegram,
                    SenderId = update.Message.Chat.Id.ToString(),
                    SenderName = update.Message.From?.Username ?? update.Message.From?.FirstName ?? update.Message.Chat.Title,
                    Text = text,
                    MessageId = update.Message.MessageId.ToString(),
                    Timestamp = update.Message.Date
                };

                await botClient.SendChatActionAsync(update.Message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
                await _automationEngine.ProcessInboundMessageAsync(inbound);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Telegram update error: {ex.Message}", "Telegram", ex.ToString());
            }
        }

        private async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            if (IsPollingConflict(exception))
            {
                LastError = "Telegram polling conflict: token bot sedang dipakai proses lain. Polling Telegram dihentikan agar log tidak spam.";
                IsRunning = false;
                _cancellationTokenSource?.Cancel();

                if (_lastPollingConflictLoggedAt == null ||
                    DateTime.Now - _lastPollingConflictLoggedAt.Value > TimeSpan.FromMinutes(5))
                {
                    _lastPollingConflictLoggedAt = DateTime.Now;
                    await _loggingService.LogWarningAsync(LastError, "Telegram", exception.ToString());
                }

                return;
            }

            await _loggingService.LogErrorAsync($"Telegram polling error: {exception.Message}", "Telegram", exception.ToString());
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            try
            {
                if (callbackQuery.Message == null)
                {
                    await botClient.AnswerCallbackQueryAsync(
                        callbackQuery.Id,
                        "Aksi tidak dikenal.",
                        cancellationToken: cancellationToken);
                    return;
                }

                long chatId = callbackQuery.Message.Chat.Id;
                if (!IsChatAllowed(chatId))
                {
                    await botClient.AnswerCallbackQueryAsync(
                        callbackQuery.Id,
                        "Akses ditolak.",
                        cancellationToken: cancellationToken);
                    return;
                }

                string data = callbackQuery.Data ?? string.Empty;
                string role = GetTelegramUserRole(chatId);

                if (data.StartsWith("menu_", StringComparison.OrdinalIgnoreCase))
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                    string menuType = data switch
                    {
                        "menu_main" => "main",
                        "menu_operasional" => "operasional",
                        "menu_laporan" => "laporan",
                        "menu_stok" => "stok",
                        "menu_ocr" => "ocr",
                        "menu_pelanggan" => "pelanggan",
                        "menu_dokumen" => "dokumen",
                        "menu_shadow" => "shadow",
                        "menu_export" => "export",
                        "menu_aksi" => "aksi",
                        "menu_help" => "help",
                        _ => string.Empty
                    };

                    if (data == "menu_help_lengkap")
                    {
                        await ProcessCallbackCommandAsync(botClient, callbackQuery, "/help lengkap", cancellationToken);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(menuType))
                    {
                        await botClient.SendTextMessageAsync(chatId, "Aksi tidak dikenal.", cancellationToken: cancellationToken);
                        return;
                    }

                    await botClient.SendTextMessageAsync(
                        chatId,
                        AutomationEngine.BuildMenuHeaderText(menuType),
                        replyMarkup: BuildMenuKeyboard(menuType, role),
                        cancellationToken: cancellationToken);
                    return;
                }

                if (data == "cancel_input")
                {
                    _automationEngine.CancelPendingInput(ChannelType.Telegram, chatId.ToString());
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Input dibatalkan.", cancellationToken: cancellationToken);
                    await botClient.SendTextMessageAsync(chatId, "Input dibatalkan.", cancellationToken: cancellationToken);
                    return;
                }

                if (TryResolveInputAction(data, out string inputAction))
                {
                    _automationEngine.SetPendingInput(ChannelType.Telegram, chatId.ToString(), inputAction);
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Silakan isi data.", cancellationToken: cancellationToken);
                    await botClient.SendTextMessageAsync(
                        chatId,
                        AutomationEngine.BuildPendingInputPrompt(inputAction),
                        replyMarkup: BuildMenuKeyboard("cancel_input", role),
                        cancellationToken: cancellationToken);
                    return;
                }

                string command = ResolveDirectCommand(data);
                if (string.IsNullOrWhiteSpace(command))
                {
                    await botClient.AnswerCallbackQueryAsync(
                        callbackQuery.Id,
                        "Aksi tidak dikenal.",
                        cancellationToken: cancellationToken);
                    return;
                }

                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    command == "/confirm" ? "Konfirmasi diproses." : "Aksi diproses.",
                    cancellationToken: cancellationToken);

                try
                {
                    await botClient.EditMessageReplyMarkupAsync(
                        chatId,
                        callbackQuery.Message.MessageId,
                        replyMarkup: null,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // Ignore UI cleanup failures; command processing is the priority.
                }

                await ProcessCallbackCommandAsync(botClient, callbackQuery, command, cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Telegram callback error: {ex.Message}", "Telegram", ex.ToString());
            }
        }

        private async Task ProcessCallbackCommandAsync(
            ITelegramBotClient botClient,
            CallbackQuery callbackQuery,
            string command,
            CancellationToken cancellationToken)
        {
            if (callbackQuery.Message == null)
            {
                return;
            }

            var inbound = new InboundMessage
            {
                Channel = ChannelType.Telegram,
                SenderId = callbackQuery.Message.Chat.Id.ToString(),
                SenderName = callbackQuery.From?.Username ?? callbackQuery.From?.FirstName ?? callbackQuery.Message.Chat.Title,
                Text = command,
                MessageId = $"callback:{callbackQuery.Id}",
                CorrelationId = callbackQuery.Id,
                PayloadHash = callbackQuery.Data,
                Timestamp = DateTime.Now
            };

            await botClient.SendChatActionAsync(callbackQuery.Message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
            await _automationEngine.ProcessInboundMessageAsync(inbound);
        }

        private async Task<string?> DownloadPhotoAsync(ITelegramBotClient botClient, string fileId, CancellationToken cancellationToken)
        {
            try
            {
                var file = await botClient.GetFileAsync(fileId, cancellationToken);
                string extension = System.IO.Path.GetExtension(file.FilePath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".jpg";
                }

                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ssa_ocr_{Guid.NewGuid():N}{extension}");
                await using var stream = System.IO.File.Create(tempPath);
                await botClient.DownloadFileAsync(file.FilePath!, stream, cancellationToken);
                return tempPath;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Gagal download foto Telegram: {ex.Message}", "Telegram", ex.ToString());
                return null;
            }
        }

        public static string? ValidateBotToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token) || token == "YOUR_TELEGRAM_BOT_TOKEN")
            {
                return "Telegram bot token belum diisi.";
            }

            string trimmed = token.Trim();
            if (!Regex.IsMatch(trimmed, @"^\d{5,}:[A-Za-z0-9_-]{20,}$"))
            {
                return "Format Telegram bot token tidak valid. Harus mengikuti pola angka:secret dari BotFather.";
            }

            return null;
        }

        private static ParseMode? ResolveParseMode(string? parseMode)
        {
            if (string.IsNullOrWhiteSpace(parseMode))
            {
                return null;
            }

            return Enum.TryParse<ParseMode>(parseMode, ignoreCase: true, out var resolved)
                ? resolved
                : null;
        }

        private InlineKeyboardMarkup? BuildReplyMarkup(OutboundMessageRecord message, long chatId)
        {
            if (ShouldAttachConfirmationKeyboard(message))
            {
                return BuildConfirmationKeyboard();
            }

            if (string.IsNullOrWhiteSpace(message.MenuKeyboardType))
            {
                return null;
            }

            return BuildMenuKeyboard(message.MenuKeyboardType, GetTelegramUserRole(chatId));
        }

        private bool IsChatAllowed(long chatId)
        {
            var telegram = _configService.Config?.Telegram;
            bool isOwner = telegram?.OwnerChatIds?.Contains(chatId) == true;
            bool isKasir = telegram?.KasirChatIds?.Contains(chatId) == true;

            if (isOwner || isKasir)
            {
                return true;
            }

            var allowedChatIds = telegram?.AllowedChatIds;
            return allowedChatIds == null || !allowedChatIds.Any() || allowedChatIds.Contains(chatId);
        }

        private string GetTelegramUserRole(long chatId)
        {
            var telegram = _configService.Config?.Telegram;
            if (telegram?.OwnerChatIds?.Contains(chatId) == true ||
                (telegram?.OwnerChatIds == null || !telegram.OwnerChatIds.Any()) &&
                telegram?.AllowedChatIds?.Contains(chatId) == true)
            {
                return "Owner";
            }

            if (telegram?.KasirChatIds?.Contains(chatId) == true)
            {
                return "Kasir";
            }

            return "Guest";
        }

        private static bool IsOwnerRole(string userRole)
        {
            return string.Equals(userRole, "Owner", StringComparison.OrdinalIgnoreCase);
        }

        private static InlineKeyboardMarkup BuildMenuKeyboard(string menuType, string userRole)
        {
            bool isOwner = IsOwnerRole(userRole);
            var rows = new List<InlineKeyboardButton[]>();

            static InlineKeyboardButton Btn(string text, string data) => InlineKeyboardButton.WithCallbackData(text, data);
            static InlineKeyboardButton[] Row(params InlineKeyboardButton[] buttons) => buttons;
            void Back() => rows.Add(Row(Btn("\u2B05\uFE0F Menu Utama", "menu_main")));

            switch ((menuType ?? string.Empty).ToLowerInvariant())
            {
                case "start":
                    rows.Add(Row(Btn("\U0001F4CB Menu Utama", "menu_main"), Btn("\u2753 Help", "menu_help")));
                    rows.Add(Row(Btn("\U0001F4DC Help Lengkap", "menu_help_lengkap")));
                    break;
                case "main":
                    rows.Add(Row(Btn("\U0001F680 Operasional Cepat", "menu_operasional"), Btn("\U0001F4CA Laporan & Analisa", "menu_laporan")));
                    rows.Add(Row(Btn("\U0001F4E6 Stok & Inventory", "menu_stok"), Btn("\U0001F6D2 Pembelian & OCR", "menu_ocr")));
                    rows.Add(Row(Btn("\U0001F465 Pelanggan & Piutang", "menu_pelanggan"), Btn("\U0001F9FE Dokumen & Riwayat", "menu_dokumen")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F9E9 Shadow Stock", "menu_shadow"), Btn("\u2B07\uFE0F Export Data", "menu_export")));
                    }
                    rows.Add(Row(Btn("\u2699\uFE0F Aksi Pending", "menu_aksi"), Btn("\u2753 Bantuan", "menu_help")));
                    break;
                case "operasional":
                    rows.Add(Row(Btn("\U0001F4CA Laporan Hari Ini", "direct_laporan"), Btn("\U0001F4E6 Stok Kritis", "direct_notif_stok")));
                    rows.Add(Row(Btn("\U0001F4B3 Piutang", "direct_piutang"), Btn("\U0001F451 Pelanggan Loyal", "direct_pelanggan_loyal")));
                    rows.Add(Row(Btn("\U0001F4F7 OCR Struk", "input_ocr_foto"), Btn("\u2705 Confirm", "direct_confirm")));
                    Back();
                    break;
                case "laporan":
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F4CA Laporan Hari Ini", "direct_laporan"), Btn("\U0001F4C8 Statistik", "direct_statistik")));
                    }
                    else
                    {
                        rows.Add(Row(Btn("\U0001F4CA Laporan Hari Ini", "direct_laporan")));
                    }
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F9E0 Analisa Bisnis", "direct_analisa"), Btn("\U0001F4E6 Rekomendasi", "direct_rekomendasi")));
                        rows.Add(Row(Btn("\U0001F464 Laporan Kasir", "direct_laporan_kasir")));
                    }
                    Back();
                    break;
                case "stok":
                    rows.Add(Row(Btn("\U0001F50D Cek Stok", "input_cek_stok"), Btn("\U0001F6A8 Stok Kritis", "direct_notif_stok")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F4CA Analisa Stok", "direct_analisa_stok"), Btn("\U0001F422 Slow Moving", "direct_slow_moving")));
                        rows.Add(Row(Btn("\U0001F5C3\uFE0F Dead Stock", "direct_dead_stock"), Btn("\U0001F3E5 Sleeping Stock", "direct_sleeping_stock")));
                    }
                    rows.Add(Row(Btn("\U0001F9EE Koreksi Stok", "input_inventory"), Btn("\U0001F3F7\uFE0F Stok Kategori", "input_stok_kategori")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F4B0 Cek Modal", "direct_cek_modal")));
                    }
                    Back();
                    break;
                case "ocr":
                    rows.Add(Row(Btn("\U0001F4F7 OCR Foto", "input_ocr_foto"), Btn("\U0001F9FE Input Teks Faktur", "input_struk_teks")));
                    rows.Add(Row(Btn("\U0001F4E6 Restock", "input_restock"), Btn("\U0001F4CB Riwayat Restock", "input_riwayat_rest")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\u2705 Selesai Struk", "direct_selesai_struk"), Btn("\U0001F4E6 Rekomendasi", "direct_rekomendasi")));
                    }
                    else
                    {
                        rows.Add(Row(Btn("\u2705 Selesai Struk", "direct_selesai_struk")));
                    }
                    Back();
                    break;
                case "pelanggan":
                    rows.Add(Row(Btn("\U0001F465 Cari Pelanggan", "input_pelanggan"), Btn("\U0001F4B3 Piutang", "input_piutang")));
                    rows.Add(Row(Btn("\U0001F451 Pelanggan Loyal", "direct_pelanggan_loyal"), Btn("\u26A0\uFE0F At Risk", "direct_at_risk")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F464 Laporan Kasir", "direct_laporan_kasir")));
                    }
                    Back();
                    break;
                case "dokumen":
                    rows.Add(Row(Btn("\U0001F4C4 Cek Dokumen", "input_cek_dokumen"), Btn("\U0001F9FE Detail Nota", "input_detail_nota")));
                    rows.Add(Row(Btn("\U0001F4CB Riwayat Restock", "input_riwayat_rest"), Btn("\U0001F4CB Riwayat Inventory", "input_riwayat_inv")));
                    rows.Add(Row(Btn("\U0001F4CA Penjualan Produk", "input_penjualan"), Btn("\u23F3 Cek Expired", "direct_cek_expired")));
                    Back();
                    break;
                case "shadow":
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F9EE Stok Efektif", "input_stok_efektif"), Btn("\U0001F9E9 Shadow Belum Mapping", "direct_shadow_stok")));
                        rows.Add(Row(Btn("\U0001F517 Set Family", "input_set_family")));
                    }
                    Back();
                    break;
                case "export":
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\u2B07\uFE0F Export Lengkap", "direct_ekspor_lengkap"), Btn("\U0001F4CA Statistik", "direct_statistik")));
                        rows.Add(Row(Btn("\U0001F9E0 Analisa", "direct_analisa"), Btn("\U0001F4E6 Rekomendasi", "direct_rekomendasi")));
                    }
                    Back();
                    break;
                case "aksi":
                    rows.Add(Row(Btn("\u2705 Confirm", "direct_confirm"), Btn("\u274C Batal", "direct_batal")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F4BE Simpan", "direct_simpan"), Btn("\U0001F4B0 Simpan + Harga Jual", "direct_simpan_jual")));
                        rows.Add(Row(Btn("\U0001F50D Detail Harga", "direct_detail_harga"), Btn("\u23ED\uFE0F Lewati Harga", "direct_lewati_harga")));
                    }
                    Back();
                    break;
                case "help":
                    rows.Add(Row(Btn("\U0001F680 Mulai Cepat", "direct_help_mulai_cepat"), Btn("\U0001F4E6 Stok", "direct_help_stok")));
                    rows.Add(Row(Btn("\U0001F6D2 OCR", "direct_help_ocr"), Btn("\U0001F465 Pelanggan", "direct_help_pelanggan")));
                    rows.Add(Row(Btn("\U0001F9FE Dokumen", "direct_help_dokumen"), Btn("\u2699\uFE0F Aksi", "direct_help_aksi")));
                    if (isOwner)
                    {
                        rows.Add(Row(Btn("\U0001F9E9 Shadow", "direct_help_shadow"), Btn("\u2B07\uFE0F Export", "direct_help_export")));
                    }
                    rows.Add(Row(Btn("\U0001F4DC Help Lengkap", "menu_help_lengkap")));
                    Back();
                    break;
                case "cancel_input":
                    rows.Add(Row(Btn("\u274C Batal Input", "cancel_input")));
                    break;
                default:
                    rows.Add(Row(Btn("\U0001F4CB Menu Utama", "menu_main")));
                    break;
            }

            return new InlineKeyboardMarkup(rows);
        }

        private static bool TryResolveInputAction(string callbackData, out string action)
        {
            action = callbackData switch
            {
                "input_cek_dokumen" => "cek_dokumen",
                "input_detail_nota" => "detail_nota",
                "input_cek_stok" => "cek_stok",
                "input_inventory" => "inventory",
                "input_restock" => "restock",
                "input_struk_teks" => "input_struk",
                "input_ocr_foto" => "ocr_foto",
                "input_riwayat_rest" => "riwayat_restock",
                "input_riwayat_inv" => "riwayat_inventory",
                "input_penjualan" => "penjualan",
                "input_stok_kategori" => "stok_kategori",
                "input_stok_efektif" => "stok_efektif",
                "input_set_family" => "set_family",
                "input_pelanggan" => "pelanggan",
                "input_piutang" => "piutang",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(action);
        }

        private static string ResolveDirectCommand(string callbackData)
        {
            return callbackData switch
            {
                "confirm_pending" => "/confirm",
                "cancel_pending" => "/cancel",
                "direct_laporan" => "/laporan",
                "direct_notif_stok" => "/notifikasi_stok",
                "direct_piutang" => "/piutang",
                "direct_pelanggan_loyal" => "/pelanggan_loyal",
                "direct_confirm" => "/confirm",
                "direct_simpan" => "/simpan",
                "direct_simpan_jual" => "/simpan_jual",
                "direct_detail_harga" => "/detail_harga",
                "direct_lewati_harga" => "/lewati_harga",
                "direct_batal" => "/batal",
                "direct_cek_expired" => "/cek_expired",
                "direct_shadow_stok" => "/shadow_stok",
                "direct_analisa_stok" => "/analisa_stok",
                "direct_slow_moving" => "/slow_moving",
                "direct_dead_stock" => "/dead_stock",
                "direct_sleeping_stock" => "/sleeping_stock",
                "direct_cek_modal" => "/cek_modal",
                "direct_at_risk" => "/pelanggan at_risk",
                "direct_laporan_kasir" => "/laporan_kasir",
                "direct_selesai_struk" => "/selesai_struk",
                "direct_ekspor_lengkap" => "/ekspor_lengkap",
                "direct_rekomendasi" => "/rekomendasi_restock",
                "direct_statistik" => "/statistik",
                "direct_analisa" => "/analisa",
                "direct_help_mulai_cepat" => "/help mulai_cepat",
                "direct_help_laporan" => "/help laporan",
                "direct_help_stok" => "/help stok",
                "direct_help_ocr" => "/help ocr",
                "direct_help_pelanggan" => "/help pelanggan",
                "direct_help_dokumen" => "/help dokumen",
                "direct_help_shadow" => "/help shadow",
                "direct_help_export" => "/help export",
                "direct_help_aksi" => "/help aksi",
                _ => string.Empty
            };
        }

        private static bool ShouldAttachConfirmationKeyboard(OutboundMessageRecord message)
        {
            if (message.RequiresConfirmation)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                return false;
            }

            return message.Text.StartsWith("Konfirmasi restock:", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("Konfirmasi inventory:", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("Konfirmasi bulk restock:", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("Konfirmasi bulk inventory:", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("📄 OCR STRUK TERBACA", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("📦 KONFIRMASI INVENTORY", StringComparison.OrdinalIgnoreCase) ||
                   message.Text.StartsWith("📦 KONFIRMASI INVENTORY BULK", StringComparison.OrdinalIgnoreCase);
        }

        private static InlineKeyboardMarkup BuildConfirmationKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("YA", "confirm_pending"),
                    InlineKeyboardButton.WithCallbackData("BATAL", "cancel_pending")
                }
            });
        }

        private static string BuildUnsupportedMessage(MessageType messageType)
        {
            return messageType switch
            {
                MessageType.Voice => "Voice note diterima, tetapi transkripsi belum diaktifkan di build ini.",
                MessageType.Document => "Dokumen diterima, tetapi belum ada handler untuk file ini.",
                MessageType.Sticker => "Sticker diterima. Gunakan pesan teks agar bot bisa memprosesnya.",
                _ => "Pesan non-teks diterima, tetapi handler media belum diaktifkan."
            };
        }

        public static bool IsBotTokenFormatValid(string? token) => string.IsNullOrWhiteSpace(ValidateBotToken(token));

        private bool TryAcquireSingleInstanceMutex(string botToken)
        {
            ReleaseSingleInstanceMutex();

            string tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(botToken))).ToLowerInvariant();
            _singleInstanceMutex = new Mutex(initiallyOwned: false, name: $@"Local\SmartSembakoAssistant.Telegram.{tokenHash}");

            try
            {
                _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(TimeSpan.Zero);
                return _ownsSingleInstanceMutex;
            }
            catch (AbandonedMutexException)
            {
                _ownsSingleInstanceMutex = true;
                return true;
            }
        }

        private void ReleaseSingleInstanceMutex()
        {
            if (_singleInstanceMutex == null)
            {
                return;
            }

            try
            {
                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
            }
            catch
            {
            }
            finally
            {
                _ownsSingleInstanceMutex = false;
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }

        private static bool IsPollingConflict(Exception exception)
        {
            return exception is ApiRequestException { ErrorCode: 409 } ||
                   exception.Message.Contains("terminated by other getUpdates request", StringComparison.OrdinalIgnoreCase) ||
                   exception.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase);
        }
    }
}
