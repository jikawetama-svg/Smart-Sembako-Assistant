namespace SmartSembakoAssistant.Services
{
    public class MessageRouter
    {
        private readonly CommandHandler _commandHandler;
        private readonly LoggingService _loggingService;

        public MessageRouter(
            CommandHandler commandHandler,
            LoggingService loggingService)
        {
            _commandHandler = commandHandler;
            _loggingService = loggingService;
        }

        public async Task<string> RouteMessageAsync(string channel, string sender, string message, string? imageUrl = null)
        {
            await _loggingService.LogInfoAsync(
                $"Routing message from {channel} - {sender}: {message}",
                "Router");

            // Handle image (OCR) if present
            if (!string.IsNullOrEmpty(imageUrl))
            {
                return "OCR belum diaktifkan. Gunakan command teks untuk saat ini.";
            }

            // Handle text command/message
            return await _commandHandler.HandleCommandAsync(message, sender, channel, true);
        }
    }
}
