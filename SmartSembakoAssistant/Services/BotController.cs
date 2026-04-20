using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartSembakoAssistant.Services
{
    /// <summary>
    /// Bot state enumerator
    /// </summary>
    public enum BotState
    {
        Stopped,     // Bot tidak jalan
        Starting,    // Bot sedang start
        Running,     // Bot aktif & polling
        Stopping,    // Bot sedang stop
        Error        // Bot error/crash
    }

    /// <summary>
    /// Bot Controller - Manage bot lifecycle dengan state management
    /// </summary>
    public class BotController
    {
        private BotState _state = BotState.Stopped;
        private DateTime? _startTime;
        private CancellationTokenSource? _cts;
        private TelegramBotService? _telegramService;
        private WhatsAppHandler? _whatsAppService;
        private MessageRouter? _messageRouter;
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private GroqService? _groqService;
        private PosDbService? _posDbService;
        private readonly DatabaseService _databaseService;

        public BotState State => _state;

        public TimeSpan? Uptime => _startTime != null
            ? DateTime.Now - _startTime
            : null;

        public bool IsRunning => _state == BotState.Running;

        // Event untuk UI update
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

        /// <summary>
        /// Start bot dengan state management
        /// </summary>
        public async Task<bool> StartAsync()
        {
            if (_state == BotState.Running)
            {
                await _loggingService.LogInfoAsync("Bot sudah running, skip start", "Bot");
                return true; // Sudah jalan
            }

            _state = BotState.Starting;
            OnStateChanged?.Invoke(this, _state);

            try
            {
                // Initialize services jika belum ada
                if (_telegramService == null)
                {
                    _groqService = new GroqService(_configService, _loggingService);

                    _telegramService = new TelegramBotService(
                        _configService,
                        _groqService!,
                        _databaseService,
                        _loggingService,
                        _posDbService);

                    var commandHandler = new CommandHandler(
                        _groqService!,
                        _databaseService,
                        _loggingService,
                        _posDbService);

                    _messageRouter = new MessageRouter(
                        commandHandler,
                        _loggingService);

                    _whatsAppService = new WhatsAppHandler(
                        _configService,
                        _loggingService,
                        _messageRouter);
                }

                _cts = new CancellationTokenSource();
                _startTime = DateTime.Now;

                // Start Telegram bot
                bool telegramStarted = await _telegramService.StartAsync();

                // Start WhatsApp handler
                bool whatsAppStarted = await _whatsAppService.StartAsync();
                
                if (telegramStarted && whatsAppStarted)
                {
                    _state = BotState.Running;
                    OnStateChanged?.Invoke(this, _state);
                    await _loggingService.LogInfoAsync("Bot (Telegram + WhatsApp) started successfully", "Bot");
                    return true;
                }
                else
                {
                    _state = BotState.Error;
                    OnStateChanged?.Invoke(this, _state);
                    await _loggingService.LogErrorAsync("Bot start failed", "Bot");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _state = BotState.Error;
                OnStateChanged?.Invoke(this, _state);
                await _loggingService.LogErrorAsync($"Bot start exception: {ex.Message}", "Bot");
                return false;
            }
        }

        /// <summary>
        /// Stop bot dengan graceful shutdown
        /// </summary>
        public async Task StopAsync()
        {
            if (_state != BotState.Running && _state != BotState.Error)
            {
                await _loggingService.LogInfoAsync("Bot tidak sedang running, skip stop", "Bot");
                return;
            }

            _state = BotState.Stopping;
            OnStateChanged?.Invoke(this, _state);

            try
            {
                // Cancel token untuk stop polling
                _cts?.Cancel();

                if (_telegramService != null)
                {
                    await _telegramService.StopAsync();
                }

                if (_whatsAppService != null)
                {
                    await _whatsAppService.StopAsync();
                }

                _state = BotState.Stopped;
                _startTime = null;
                OnStateChanged?.Invoke(this, _state);
                await _loggingService.LogInfoAsync("Bot stopped", "Bot");
            }
            catch (Exception ex)
            {
                _state = BotState.Error;
                OnStateChanged?.Invoke(this, _state);
                await _loggingService.LogErrorAsync($"Bot stop exception: {ex.Message}", "Bot");
            }
        }

        /// <summary>
        /// Restart bot (stop lalu start lagi)
        /// </summary>
        public async Task<bool> RestartAsync()
        {
            await _loggingService.LogInfoAsync("Restarting bot...", "Bot");
            await StopAsync();
            await Task.Delay(1000); // Wait 1s for cleanup
            return await StartAsync();
        }

        /// <summary>
        /// Get bot service instance untuk operations
        /// </summary>
        public TelegramBotService? GetBotService()
        {
            return _botService;
        }
    }
}