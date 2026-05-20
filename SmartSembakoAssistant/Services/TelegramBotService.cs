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
            }
            finally
            {
                _lifecycleLock.Release();
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
                replyMarkup: ShouldAttachConfirmationKeyboard(message)
                    ? BuildConfirmationKeyboard()
                    : null);

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
                string command = callbackQuery.Data switch
                {
                    "confirm_pending" => "/confirm",
                    "cancel_pending" => "/cancel",
                    _ => string.Empty
                };

                if (string.IsNullOrWhiteSpace(command) || callbackQuery.Message == null)
                {
                    await botClient.AnswerCallbackQueryAsync(
                        callbackQuery.Id,
                        "Aksi tidak dikenal.",
                        cancellationToken: cancellationToken);
                    return;
                }

                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    command == "/confirm" ? "Konfirmasi diproses." : "Aksi dibatalkan.",
                    cancellationToken: cancellationToken);

                try
                {
                    await botClient.EditMessageReplyMarkupAsync(
                        callbackQuery.Message.Chat.Id,
                        callbackQuery.Message.MessageId,
                        replyMarkup: null,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // Ignore UI cleanup failures; command processing is the priority.
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

                await _automationEngine.ProcessInboundMessageAsync(inbound);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Telegram callback error: {ex.Message}", "Telegram", ex.ToString());
            }
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
