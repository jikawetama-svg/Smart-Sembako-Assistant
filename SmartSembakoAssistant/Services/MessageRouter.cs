using System.Threading.Tasks;
using SmartSembakoAssistant.Models;

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
                return await HandleOcrAsync(imageUrl, sender, channel);
            }

            // Handle text command/message
            return await _commandHandler.HandleCommandAsync(message, sender, channel);
        }

        private async Task<string> HandleOcrAsync(string imageUrl, string sender, string channel)
        {
            // TODO: Implement OCR logic
            // For now, placeholder
            await _loggingService.LogInfoAsync($"OCR request from {sender} in {channel}", "OCR");
            return "Fitur OCR akan diimplementasikan. Kirim teks command saja untuk sekarang.";
        }
    }
}