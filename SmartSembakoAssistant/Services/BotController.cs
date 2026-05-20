using System.Threading;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public enum BotState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    public class BotController
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private GroqService? _groqService;
        private AutomationEngine? _automationEngine;
        private TelegramBotService? _telegramService;
        private WhatsAppHandler? _whatsAppService;
        private BaileysSidecarService? _baileysService;
        private TunnelManager? _tunnelManager;
        private PeriodicTimer? _automationTimer;
        private PeriodicTimer? _outboxTimer;
        private CancellationTokenSource? _workerCts;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly Queue<DateTime> _baileysSentAt = new();
        private DateTime? _startTime;

        public BotState State { get; private set; } = BotState.Stopped;
        public TimeSpan? Uptime => _startTime == null ? null : DateTime.Now - _startTime.Value;
        public bool IsRunning => State == BotState.Running;
        public bool IsTelegramRunning => _telegramService?.IsRunning == true;
        public bool IsWhatsAppRunning => _whatsAppService?.IsRunning == true;
        public bool IsBaileysRunning => _baileysService?.IsRunning == true;
        public bool IsTunnelRunning => _tunnelManager?.IsRunning == true;
        public string? TunnelPublicUrl => _tunnelManager?.CurrentPublicUrl ?? _configService.Config?.Tunnel?.PublicUrl;
        public string? LastStartError { get; private set; }

        public event EventHandler<BotState>? OnStateChanged;

        public BotController(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService = null)
        {
            _configService = configService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
        }

        public async Task<bool> StartAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (State == BotState.Running)
                {
                    return true;
                }

                if (State != BotState.Stopped)
                {
                    await StopCoreAsync();
                }

                LastStartError = null;
                State = BotState.Starting;
                OnStateChanged?.Invoke(this, State);

                _groqService ??= new GroqService(_configService, _loggingService);
                _automationEngine ??= new AutomationEngine(_configService, _groqService, _databaseService, _loggingService, _posDbService);
                _telegramService ??= new TelegramBotService(_configService, _loggingService, _automationEngine);
                _automationEngine.RegisterTelegramDocumentSender((chatId, filePath, caption) =>
                    _telegramService.SendDocumentAsync(chatId, filePath, caption));
                _whatsAppService ??= new WhatsAppHandler(_configService, _loggingService, _automationEngine);
                _baileysService ??= new BaileysSidecarService(_configService, _loggingService);
                _tunnelManager ??= new TunnelManager(_configService, _loggingService);

                bool telegramStarted = true;
                bool whatsAppStarted = true;
                bool baileysStarted = true;
                bool tunnelStarted = true;
                string whatsAppMode = WhatsAppModes.Normalize(_configService.Config?.WhatsApp?.Mode);
                string? telegramTokenError = TelegramBotService.ValidateBotToken(_configService.Config?.Telegram?.BotToken);

                if (!string.IsNullOrWhiteSpace(_configService.Config?.Telegram?.BotToken) &&
                    _configService.Config?.Telegram?.BotToken != "YOUR_TELEGRAM_BOT_TOKEN")
                {
                    if (!string.IsNullOrWhiteSpace(telegramTokenError))
                    {
                        telegramStarted = false;
                        LastStartError = telegramTokenError;
                        await _loggingService.LogWarningAsync(telegramTokenError, "Telegram");
                    }
                    else
                    {
                        telegramStarted = await _telegramService.StartAsync();
                        if (!telegramStarted)
                        {
                            LastStartError = _telegramService.LastError;
                        }
                    }
                }

                if ((_configService.Config?.WhatsApp?.Enabled == true && WhatsAppModes.UsesCloudApi(whatsAppMode)) ||
                    (_configService.Config?.Baileys?.Enabled == true && WhatsAppModes.UsesBaileys(whatsAppMode)))
                {
                    whatsAppStarted = await _whatsAppService.StartAsync();
                    if (whatsAppStarted && _configService.Config?.WhatsApp?.Enabled == true && WhatsAppModes.UsesCloudApi(whatsAppMode))
                    {
                        tunnelStarted = await _tunnelManager.StartAsync(_whatsAppService.LocalPort);
                    }
                }

                if (_configService.Config?.Baileys?.Enabled == true && WhatsAppModes.UsesBaileys(whatsAppMode))
                {
                    baileysStarted = await _baileysService.StartAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090);
                }

                bool anyTransportActive = _telegramService.IsRunning || _whatsAppService.IsRunning || _baileysService.IsRunning;
                if (!anyTransportActive)
                {
                    LastStartError ??= _telegramService?.LastError ?? "Tidak ada transport yang berhasil aktif.";
                    State = BotState.Error;
                    OnStateChanged?.Invoke(this, State);
                    return false;
                }

                _startTime = DateTime.Now;
                State = (telegramStarted && whatsAppStarted && baileysStarted && tunnelStarted) ? BotState.Running : BotState.Error;
                if (State == BotState.Error && string.IsNullOrWhiteSpace(LastStartError))
                {
                    LastStartError = _telegramService?.LastError;
                }
                OnStateChanged?.Invoke(this, State);
                StartWorkers();

                await _loggingService.LogInfoAsync(
                    $"Automation runtime started. Telegram={_telegramService.IsRunning}, WhatsApp={_whatsAppService.IsRunning}, Baileys={_baileysService.IsRunning}, Tunnel={_tunnelManager.IsRunning}",
                    "Bot");

                return State == BotState.Running || anyTransportActive;
            }
            catch (Exception ex)
            {
                LastStartError = ex.Message;
                State = BotState.Error;
                OnStateChanged?.Invoke(this, State);
                await _loggingService.LogErrorAsync($"Bot start exception: {ex.Message}", "Bot", ex.ToString());
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
                if (State == BotState.Stopped)
                {
                    return;
                }

                State = BotState.Stopping;
                OnStateChanged?.Invoke(this, State);
                await StopCoreAsync();
                State = BotState.Stopped;
                OnStateChanged?.Invoke(this, State);
            }
            catch (Exception ex)
            {
                State = BotState.Error;
                OnStateChanged?.Invoke(this, State);
                await _loggingService.LogErrorAsync($"Bot stop exception: {ex.Message}", "Bot", ex.ToString());
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<bool> RestartAsync()
        {
            await StopAsync();
            await Task.Delay(1000);
            return await StartAsync();
        }

        public TelegramBotService? GetBotService()
        {
            return _telegramService;
        }

        public IntegrationStatus GetIntegrationStatus()
        {
            if (_automationEngine == null)
            {
                return new IntegrationStatus
                {
                    ActiveConfigPath = _configService.ConfigPath,
                    ConfigWarning = _configService.DuplicateConfigWarning,
                    TelegramConfigured = !string.IsNullOrWhiteSpace(_configService.Config?.Telegram?.BotToken) &&
                                         _configService.Config?.Telegram?.BotToken != "YOUR_TELEGRAM_BOT_TOKEN",
                    TelegramValidated = TelegramBotService.IsBotTokenFormatValid(_configService.Config?.Telegram?.BotToken),
                    TelegramRunning = IsTelegramRunning,
                    TelegramLastError = _telegramService?.LastError,
                    TelegramLastValidatedAt = _telegramService?.LastValidatedAt,
                    WhatsAppRunning = IsWhatsAppRunning,
                    BaileysRunning = IsBaileysRunning,
                    TunnelRunning = IsTunnelRunning,
                    DatabaseConnected = _posDbService != null,
                    TelegramActionHint = _telegramService?.LastError,
                    AiConfigured = !string.IsNullOrWhiteSpace(_configService.Config?.Groq?.ApiKey),
                    WhatsAppCloudOutboundReady = _whatsAppService?.CanSendOutbound() == true,
                    WhatsAppActionHint = _whatsAppService?.BuildActionHint(),
                    BaileysOutboundReady = _baileysService?.CanSendOutbound() == true,
                    BaileysActionHint = _baileysService?.BuildActionHint(),
                    PosDbSchemaStatus = _posDbService?.SchemaStatus,
                    PosDbLastValidatedAt = _posDbService?.LastSchemaValidatedAt,
                    PosDbActionHint = _posDbService?.LastSchemaActionHint
                };
            }

            return _automationEngine.GetIntegrationStatus(
                IsTelegramRunning,
                IsWhatsAppRunning,
                IsTunnelRunning,
                TunnelPublicUrl,
                IsBaileysRunning,
                _baileysService?.IsReachable == true,
                _baileysService?.IsPaired == true,
                _configService,
                _telegramService,
                _baileysService,
                _posDbService);
        }

        private void StartWorkers()
        {
            if (_automationEngine == null)
            {
                return;
            }

            _workerCts?.Cancel();
            _workerCts = new CancellationTokenSource();
            _automationTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            _outboxTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));

            _ = Task.Run(() => RunAutomationLoopAsync(_workerCts.Token), _workerCts.Token);
            _ = Task.Run(() => RunOutboxLoopAsync(_workerCts.Token), _workerCts.Token);
        }

        private async Task StopCoreAsync()
        {
            _workerCts?.Cancel();
            _workerCts?.Dispose();
            _workerCts = null;
            _automationTimer?.Dispose();
            _automationTimer = null;
            _outboxTimer?.Dispose();
            _outboxTimer = null;

            if (_telegramService != null)
            {
                await _telegramService.StopAsync();
            }

            if (_baileysService != null)
            {
                await _baileysService.StopAsync();
            }

            if (_tunnelManager != null)
            {
                await _tunnelManager.StopAsync();
            }

            if (_whatsAppService != null)
            {
                await _whatsAppService.StopAsync();
            }

            _startTime = null;
        }

        private async Task RunAutomationLoopAsync(CancellationToken cancellationToken)
        {
            if (_automationTimer == null || _automationEngine == null)
            {
                return;
            }

            while (await _automationTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var messages = await _automationEngine.RunBackgroundAutomationAsync();
                    await _automationEngine.EnqueueOutboundMessagesAsync(messages);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Background automation error: {ex.Message}", "Automation", ex.ToString());
                }
            }
        }

        private async Task RunOutboxLoopAsync(CancellationToken cancellationToken)
        {
            if (_outboxTimer == null || _automationEngine == null)
            {
                return;
            }

            while (await _outboxTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var pending = await _automationEngine.GetDueOutboundMessagesAsync();
                    foreach (var record in pending)
                    {
                        if (record.Channel == ChannelType.WhatsApp && _whatsAppService?.CanSendOutbound() != true)
                        {
                            await _automationEngine.HandleOutboundDispatchDeferredAsync(
                                record,
                                _whatsAppService?.BuildActionHint() ?? "WhatsApp Cloud belum siap kirim.",
                                TimeSpan.FromMinutes(5));
                            continue;
                        }

                        try
                        {
                            if (record.Channel == ChannelType.Baileys)
                            {
                                if (_baileysService?.CanSendOutbound() != true)
                                {
                                    await _automationEngine.HandleOutboundDispatchDeferredAsync(
                                        record,
                                        _baileysService?.BuildActionHint() ?? "Baileys belum siap kirim.",
                                        TimeSpan.FromMinutes(5));
                                    continue;
                                }

                                await WaitForBaileysRateLimitAsync(cancellationToken);
                            }

                            string? externalId = record.Channel switch
                            {
                                ChannelType.Telegram when _telegramService?.IsRunning == true => await _telegramService.SendQueuedMessageAsync(record),
                                ChannelType.WhatsApp when _whatsAppService?.CanSendOutbound() == true => await _whatsAppService.SendQueuedMessageAsync(record),
                                ChannelType.Baileys when _baileysService?.CanSendOutbound() == true => await _baileysService.SendQueuedMessageAsync(record),
                                _ => throw new InvalidOperationException($"Transport {record.Channel} tidak aktif.")
                            };

                            await _automationEngine.HandleOutboundDispatchSuccessAsync(record, externalId);

                            if (record.Channel == ChannelType.Baileys)
                            {
                                RecordBaileysSend();
                                await DelayAfterBaileysSendAsync(cancellationToken);
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            await _automationEngine.HandleOutboundDispatchFailureAsync(record, ex.Message);
                            await _loggingService.LogWarningAsync(
                                $"Outbound {record.Channel} gagal untuk {record.RecipientId}: {ex.Message}",
                                "Outbound");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Outbox worker error: {ex.Message}", "Outbound", ex.ToString());
                }
            }
        }

        private async Task WaitForBaileysRateLimitAsync(CancellationToken cancellationToken)
        {
            int maxPerMinute = Math.Max(0, _configService.Config?.Baileys?.MaxMessagesPerMinute ?? 20);
            if (maxPerMinute == 0)
            {
                return;
            }

            while (true)
            {
                DateTime now = DateTime.UtcNow;
                PruneBaileysSendHistory(now);
                if (_baileysSentAt.Count < maxPerMinute)
                {
                    return;
                }

                TimeSpan wait = _baileysSentAt.Peek().AddMinutes(1) - now;
                if (wait < TimeSpan.FromMilliseconds(250))
                {
                    wait = TimeSpan.FromMilliseconds(250);
                }

                await Task.Delay(wait, cancellationToken);
            }
        }

        private async Task DelayAfterBaileysSendAsync(CancellationToken cancellationToken)
        {
            var settings = _configService.Config?.Baileys;
            int minDelayMs = Math.Max(0, settings?.MessageDelayMinMs ?? 1500);
            int maxDelayMs = Math.Max(minDelayMs, settings?.MessageDelayMaxMs ?? 3500);
            if (maxDelayMs == 0)
            {
                return;
            }

            int delayMs = minDelayMs == maxDelayMs
                ? minDelayMs
                : Random.Shared.Next(minDelayMs, maxDelayMs + 1);

            await Task.Delay(delayMs, cancellationToken);
        }

        private void RecordBaileysSend()
        {
            DateTime now = DateTime.UtcNow;
            PruneBaileysSendHistory(now);
            _baileysSentAt.Enqueue(now);
        }

        private void PruneBaileysSendHistory(DateTime now)
        {
            DateTime cutoff = now.AddMinutes(-1);
            while (_baileysSentAt.Count > 0 && _baileysSentAt.Peek() <= cutoff)
            {
                _baileysSentAt.Dequeue();
            }
        }
    }
}
