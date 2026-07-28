using System.Threading;
using System.Collections.Concurrent;
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
        private PeriodicTimer? _dualStockWatcherTimer;
        private PeriodicTimer? _cloudCommandQueueTimer;
        private CancellationTokenSource? _workerCts;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly Queue<DateTime> _baileysSentAt = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastSentByRecipient = new();
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
                SetActiveBotRuntime(true);

                _groqService ??= new GroqService(_configService, _loggingService);
                _automationEngine ??= new AutomationEngine(_configService, _groqService, _databaseService, _loggingService, _posDbService);
                _telegramService ??= new TelegramBotService(_configService, _loggingService, _automationEngine);
                _whatsAppService ??= new WhatsAppHandler(_configService, _loggingService, _automationEngine);
                _baileysService ??= new BaileysSidecarService(_configService, _loggingService);
                _automationEngine.RegisterDocumentSender(async (channel, recipientId, filePath, caption) =>
                {
                    return channel switch
                    {
                        ChannelType.Telegram when long.TryParse(recipientId, out var chatId) && _telegramService.IsRunning
                            => await SendTelegramDocumentAsync(chatId, filePath, caption),
                        ChannelType.Baileys when _baileysService.CanSendOutbound()
                            => await _baileysService.SendDocumentAsync(recipientId, filePath, caption),
                        _ => throw new InvalidOperationException($"Transport {channel} belum siap mengirim dokumen.")
                    };
                });
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
                    SetActiveBotRuntime(false);
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
                await AutoCancelOldWhatsAppLikeOutboxOnStartupAsync();
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
                SetActiveBotRuntime(false);
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
                    BaileysSidecarBuildTag = _baileysService?.SidecarBuildTag,
                    AppInstanceId = _configService.Config?.App?.InstanceId,
                    MachineName = _configService.Config?.App?.MachineName,
                    ActiveRuntimeSince = _configService.Config?.App?.ActiveRuntimeSince,
                    PendingOutboundCount = _databaseService.GetPendingOutboundCount(),
                    PendingWhatsAppLikeOutboundCount = _databaseService.GetPendingWhatsAppLikeOutboundCount(),
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
            _dualStockWatcherTimer = new PeriodicTimer(TimeSpan.FromSeconds(_automationEngine.GetDualStockSyncIntervalSeconds()));
            _cloudCommandQueueTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            _ = Task.Run(() => RunDualStockWatcherLoopAsync(_workerCts.Token), _workerCts.Token);

            _ = Task.Run(() => RunDualStockStartupCatchUpAsync(_workerCts.Token), _workerCts.Token);
            _ = Task.Run(() => RunAutomationLoopAsync(_workerCts.Token), _workerCts.Token);
            _ = Task.Run(() => RunOutboxLoopAsync(_workerCts.Token), _workerCts.Token);
            _ = Task.Run(() => RunCloudCommandQueueLoopAsync(_workerCts.Token), _workerCts.Token);
        }

        private async Task AutoCancelOldWhatsAppLikeOutboxOnStartupAsync()
        {
            DateTime cutoff = DateTime.Now.AddMinutes(-5);
            string reason = $"startup_auto_cancel: pending WhatsApp/Baileys lebih lama dari 5 menit dibatalkan saat runtime start ({cutoff:O}).";
            var result = await _databaseService.CancelPendingWhatsAppLikeOutboxAsync(reason, cutoff);
            if (result.TotalCancelled <= 0)
            {
                return;
            }

            await _loggingService.LogWarningAsync(
                $"Auto clear outbox saat startup: {result.TotalCancelled} pending dibatalkan. WhatsApp={result.WhatsAppCancelled}, Baileys={result.BaileysCancelled}.",
                "OutboundGuard");
        }

        private async Task<string?> SendTelegramDocumentAsync(long chatId, string filePath, string caption)
        {
            if (_telegramService == null)
            {
                throw new InvalidOperationException("Bot Telegram belum siap mengirim file.");
            }

            await _telegramService.SendDocumentAsync(chatId, filePath, caption);
            return null;
        }

        private async Task StopCoreAsync()
        {
            if (_automationEngine != null)
            {
                try
                {
                    await _automationEngine.RunDualStockShutdownSyncAsync();
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Dual stock shutdown sync gagal: {ex.Message}", "DualStockWatcher", ex.ToString());
                }
            }

            _workerCts?.Cancel();
            _workerCts?.Dispose();
            _workerCts = null;
            _automationTimer?.Dispose();
            _automationTimer = null;
            _outboxTimer?.Dispose();
            _outboxTimer = null;
            _dualStockWatcherTimer?.Dispose();
            _dualStockWatcherTimer = null;
            _cloudCommandQueueTimer?.Dispose();
            _cloudCommandQueueTimer = null;
            SetActiveBotRuntime(false);

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

        private void SetActiveBotRuntime(bool active)
        {
            var app = _configService.Config?.App;
            if (app == null)
            {
                return;
            }

            bool changed = app.IsActiveBotRuntime != active;
            app.IsActiveBotRuntime = active;
            if (active)
            {
                app.ActiveRuntimeSince = DateTime.Now;
                changed = true;
            }

            if (!active && app.ActiveRuntimeSince.HasValue)
            {
                app.ActiveRuntimeSince = null;
                changed = true;
            }

            if (changed)
            {
                _configService.SaveConfig();
            }
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
                    messages.AddRange(await _automationEngine.RunDualStockScheduledSyncAsync());
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

        private async Task RunCloudCommandQueueLoopAsync(CancellationToken cancellationToken)
        {
            if (_cloudCommandQueueTimer == null || _automationEngine == null)
            {
                return;
            }

            await ProcessCloudCommandQueueAsync(cancellationToken);

            while (await _cloudCommandQueueTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await ProcessCloudCommandQueueAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Cloud command queue error: {ex.Message}", "CloudCommandQueue", ex.ToString());
                }
            }
        }

        private async Task ProcessCloudCommandQueueAsync(CancellationToken cancellationToken)
        {
            if (_automationEngine == null)
            {
                return;
            }

            var supabase = _configService.Config?.Supabase;
            if (supabase?.Enabled != true || string.Equals(supabase.SyncMode, "read_only", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var supabaseClient = new SupabaseClient(_configService);
            var pending = await supabaseClient.GetPendingAgentCommandsAsync(limit: 5);
            if (!pending.success)
            {
                await _loggingService.LogWarningAsync($"Gagal membaca antrean command cloud: {pending.error}", "CloudCommandQueue");
                return;
            }

            string claimedBy = _configService.Config?.App?.InstanceId
                ?? Environment.MachineName
                ?? "desktop";

            foreach (var command in pending.commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(command.Id) ||
                    string.IsNullOrWhiteSpace(command.CommandText) ||
                    string.IsNullOrWhiteSpace(command.SourceChatId))
                {
                    continue;
                }

                var claim = await supabaseClient.ClaimAgentCommandAsync(command.Id, claimedBy);
                if (!claim.success)
                {
                    await _loggingService.LogWarningAsync($"Command cloud {command.Id} tidak bisa diklaim: {claim.error}", "CloudCommandQueue");
                    continue;
                }

                try
                {
                    var inbound = new InboundMessage
                    {
                        Channel = ChannelType.Telegram,
                        SenderId = command.SourceUserId ?? command.SourceChatId,
                        SenderName = "Cloud Queue",
                        Text = command.CommandText,
                        MessageId = $"cloud:{command.Id}",
                        CorrelationId = command.Id,
                        PayloadHash = $"cloud:{command.Id}",
                        ReceivedAt = DateTime.Now,
                        Timestamp = command.CreatedAt?.ToLocalTime() ?? DateTime.Now
                    };

                    var outbound = await _automationEngine.ProcessInboundMessageAsync(inbound);
                    string result = outbound?.Text ?? "Perintah cloud diproses oleh Desktop lokal.";
                    await supabaseClient.CompleteAgentCommandAsync(command.Id, result);
                    await _loggingService.LogInfoAsync($"Command cloud {command.Id} selesai: {command.CommandText}", "CloudCommandQueue");
                }
                catch (Exception ex)
                {
                    await supabaseClient.FailAgentCommandAsync(command.Id, ex.Message);
                    await _loggingService.LogErrorAsync($"Command cloud {command.Id} gagal: {ex.Message}", "CloudCommandQueue", ex.ToString());
                }
            }
        }

        private async Task RunDualStockStartupCatchUpAsync(CancellationToken cancellationToken)
        {
            if (_automationEngine == null)
            {
                return;
            }

            try
            {
                await _automationEngine.RunDualStockStartupCatchUpAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Dual stock startup catch-up error: {ex.Message}", "DualStockWatcher", ex.ToString());
            }
        }

        private async Task RunDualStockWatcherLoopAsync(CancellationToken cancellationToken)
        {
            if (_dualStockWatcherTimer == null || _automationEngine == null)
            {
                return;
            }

            while (await _dualStockWatcherTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    if (!_automationEngine.IsDualStockRealtimeWatcherEnabled())
                    {
                        continue;
                    }

                    var messages = await _automationEngine.RunDatabaseSyncWatcherAsync();
                    await _automationEngine.EnqueueOutboundMessagesAsync(messages);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Dual stock watcher error: {ex.Message}", "DualStockWatcher", ex.ToString());
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
                        if (!IsOutboundOwnedByThisRuntime(record, out string runtimeReason))
                        {
                            await _automationEngine.HandleOutboundDispatchRejectedAsync(record, runtimeReason);
                            await _loggingService.LogWarningAsync(
                                $"Outbound {record.Channel} dibatalkan karena runtime/instance tidak cocok untuk {record.RecipientId}: {runtimeReason}",
                                "OutboundGuard");
                            continue;
                        }

                        if (IsOutboundStale(record, out string staleReason))
                        {
                            await _automationEngine.HandleOutboundDispatchRejectedAsync(record, staleReason);
                            await _loggingService.LogWarningAsync(
                                $"Outbound {record.Channel} lama dibatalkan untuk {record.RecipientId}: {staleReason}",
                                "OutboundGuard");
                            continue;
                        }

                        if (!IsOutboundRecipientStillAuthorized(record, out string authReason))
                        {
                            await _automationEngine.HandleOutboundDispatchRejectedAsync(record, authReason);
                            await _loggingService.LogWarningAsync(
                                $"Outbound {record.Channel} dibatalkan untuk recipient tidak aktif {record.RecipientId}: {authReason}",
                                "OutboundGuard");
                            continue;
                        }

                        if (await _databaseService.HasSentOutboundForCorrelationRecipientAsync(record))
                        {
                            string duplicateReason = "duplicate_outbound: correlation dan recipient ini sudah pernah terkirim.";
                            await _automationEngine.HandleOutboundDispatchRejectedAsync(record, duplicateReason);
                            await _loggingService.LogWarningAsync(
                                $"Outbound {record.Channel} duplikat dibatalkan untuk {record.RecipientId}: {duplicateReason}",
                                "OutboundGuard");
                            continue;
                        }

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
                                if (_baileysService == null)
                                {
                                    await _automationEngine.HandleOutboundDispatchDeferredAsync(
                                        record,
                                        "Baileys belum siap kirim.",
                                        TimeSpan.FromMinutes(5));
                                    continue;
                                }

                                await _baileysService.RefreshHealthAsync();
                                if (!_baileysService.CanSendOutbound())
                                {
                                    await Task.Delay(750, cancellationToken);
                                    await _baileysService.RefreshHealthAsync();
                                }

                                if (!_baileysService.CanSendOutbound())
                                {
                                    await _automationEngine.HandleOutboundDispatchDeferredAsync(
                                        record,
                                        _baileysService.BuildActionHint(),
                                        TimeSpan.FromSeconds(15));
                                    continue;
                                }

                                await WaitForBaileysRateLimitAsync(cancellationToken);
                            }

                            await WaitForRecipientCooldownAsync(record, cancellationToken);

                            string? externalId = record.Channel switch
                            {
                                ChannelType.Telegram when _telegramService?.IsRunning == true => await _telegramService.SendQueuedMessageAsync(record),
                                ChannelType.WhatsApp when _whatsAppService?.CanSendOutbound() == true => await _whatsAppService.SendQueuedMessageAsync(record),
                                ChannelType.Baileys when _baileysService != null => await _baileysService.SendQueuedMessageAsync(record),
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

        private async Task WaitForRecipientCooldownAsync(OutboundMessageRecord record, CancellationToken cancellationToken)
        {
            if (record.Channel != ChannelType.Baileys && record.Channel != ChannelType.WhatsApp)
            {
                return;
            }

            string normalizedRecipient = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId);
            if (string.IsNullOrWhiteSpace(normalizedRecipient))
            {
                return;
            }

            TimeSpan cooldown = record.Channel == ChannelType.Baileys
                ? TimeSpan.FromSeconds(3)
                : TimeSpan.FromSeconds(1);
            string key = $"{record.Channel}:{normalizedRecipient}";
            DateTime now = DateTime.UtcNow;
            if (_lastSentByRecipient.TryGetValue(key, out var lastSentAt))
            {
                TimeSpan wait = lastSentAt.Add(cooldown) - now;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }
            }

            _lastSentByRecipient[key] = DateTime.UtcNow;
        }

        private bool IsOutboundOwnedByThisRuntime(OutboundMessageRecord record, out string reason)
        {
            reason = string.Empty;
            if (record.Channel != ChannelType.Baileys && record.Channel != ChannelType.WhatsApp)
            {
                return true;
            }

            var app = _configService.Config?.App;
            if (app?.IsActiveBotRuntime == false)
            {
                reason = $"inactive_runtime: instance {app.InstanceId ?? "-"} tidak ditandai aktif.";
                return false;
            }

            string currentInstanceId = app?.InstanceId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(record.AppInstanceId) &&
                !string.IsNullOrWhiteSpace(currentInstanceId) &&
                !string.Equals(record.AppInstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"different_app_instance: pesan dibuat oleh {record.AppInstanceId}, runtime aktif {currentInstanceId}.";
                return false;
            }

            return true;
        }

        private static bool IsOutboundStale(OutboundMessageRecord record, out string reason)
        {
            if (record.ExpiresAt.HasValue && DateTime.Now > record.ExpiresAt.Value)
            {
                TimeSpan expiredFor = DateTime.Now - record.ExpiresAt.Value;
                reason = $"expired_outbound: melewati expires_at {record.ExpiresAt.Value:O} sejak {FormatAge(expiredFor)}.";
                return true;
            }

            TimeSpan maxAge = GetOutboundMaxAge(record);
            TimeSpan age = DateTime.Now - record.CreatedAt;
            if (age <= maxAge)
            {
                reason = string.Empty;
                return false;
            }

            reason = $"stale_outbound: usia {FormatAge(age)} melebihi batas {FormatAge(maxAge)}.";
            return true;
        }

        private static TimeSpan GetOutboundMaxAge(OutboundMessageRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.MediaUrl) ||
                !string.Equals(record.MessageKind, "text", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromMinutes(15);
            }

            return TimeSpan.FromMinutes(2);
        }

        private bool IsOutboundRecipientStillAuthorized(OutboundMessageRecord record, out string reason)
        {
            reason = string.Empty;
            if (record.Channel != ChannelType.Baileys && record.Channel != ChannelType.WhatsApp)
            {
                return true;
            }

            string normalizedRecipient = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId);
            if (string.IsNullOrWhiteSpace(normalizedRecipient))
            {
                reason = "recipient_no_longer_authorized: nomor tujuan kosong/tidak valid.";
                return false;
            }

            var ownerNumbers = record.Channel == ChannelType.Baileys
                ? _configService.Config?.Baileys?.OwnerNumbers
                : _configService.Config?.WhatsApp?.OwnerNumbers;
            var kasirNumbers = record.Channel == ChannelType.Baileys
                ? _configService.Config?.Baileys?.KasirNumbers
                : _configService.Config?.WhatsApp?.KasirNumbers;

            var authorized = (ownerNumbers ?? new List<string>())
                .Concat(kasirNumbers ?? new List<string>())
                .Select(AutomationEngine.NormalizeWhatsAppNumber)
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!authorized.Any())
            {
                if (record.Channel == ChannelType.Baileys && _configService.Config?.Setup?.SetupCompleted == true)
                {
                    reason = "recipient_no_longer_authorized: Baileys aktif tetapi daftar owner/kasir kosong.";
                    return false;
                }

                return true;
            }

            if (authorized.Contains(normalizedRecipient, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            reason = "recipient_no_longer_authorized: nomor tujuan tidak ada di owner/kasir aktif.";
            return false;
        }

        private static string FormatAge(TimeSpan value)
        {
            if (value.TotalMinutes >= 1)
            {
                return $"{Math.Floor(value.TotalMinutes):0}m {value.Seconds:00}s";
            }

            return $"{Math.Max(0, Math.Floor(value.TotalSeconds)):0}s";
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

            if (_baileysSentAt.Count > 10)
            {
                delayMs += Random.Shared.Next(5000, 12001);
            }

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
