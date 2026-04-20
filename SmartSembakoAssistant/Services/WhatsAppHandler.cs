using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class WhatsAppHandler
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly MessageRouter _messageRouter;
        private HttpListener? _listener;
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;

        public WhatsAppHandler(
            ConfigService configService,
            LoggingService loggingService,
            MessageRouter messageRouter)
        {
            _configService = configService;
            _loggingService = loggingService;
            _messageRouter = messageRouter;
        }

        public async Task<bool> StartAsync()
        {
            try
            {
                string? port = _configService.Config?.WhatsApp?.BridgePort ?? "8080";
                string url = $"http://localhost:{port}/whatsapp/";

                _listener = new HttpListener();
                _listener.Prefixes.Add(url);

                _listener.Start();
                _isRunning = true;

                await _loggingService.LogInfoAsync($"WhatsApp Handler started on {url}", "WhatsApp");

                // Start listening in background
                Task.Run(() => ListenAsync());

                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Failed to start WhatsApp Handler: {ex.Message}",
                    "WhatsApp",
                    ex.ToString());
                _isRunning = false;
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _listener?.Stop();
                _isRunning = false;
                await _loggingService.LogInfoAsync("WhatsApp Handler stopped", "WhatsApp");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error stopping WhatsApp Handler: {ex.Message}",
                    "WhatsApp",
                    ex.ToString());
            }
        }

        public async Task SendMessageAsync(string recipient, string message)
        {
            try
            {
                // Assume bridge has send endpoint
                string bridgeUrl = _configService.Config?.WhatsApp?.BridgeUrl ?? "http://localhost:8080/whatsapp/send";

                var payload = new
                {
                    to = recipient,
                    message = message
                };

                using var client = new HttpClient();
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(bridgeUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    await _loggingService.LogInfoAsync($"Message sent to {recipient}", "WhatsApp");
                }
                else
                {
                    await _loggingService.LogErrorAsync($"Failed to send message to {recipient}", "WhatsApp");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error sending WhatsApp message: {ex.Message}",
                    "WhatsApp",
                    ex.ToString());
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                string json = await reader.ReadToEndAsync();

                var message = JsonSerializer.Deserialize<WhatsAppMessage>(json);
                if (message != null)
                {
                    string response = await _messageRouter.RouteMessageAsync("WhatsApp", message.Sender, message.Text, message.ImageUrl);

                    // Send response back to WhatsApp
                    await SendMessageAsync(message.Sender, response);
                }

                // Respond OK
                context.Response.StatusCode = 200;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error processing WhatsApp request: {ex.Message}",
                    "WhatsApp",
                    ex.ToString());
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }

        private async Task HandleMessageAsync(WhatsAppMessage message)
        {
            await _messageRouter.RouteMessageAsync("WhatsApp", message.Sender, message.Text, message.ImageUrl);
        }
    }

    public class WhatsAppMessage
    {
        public string Sender { get; set; } = "";
        public string Text { get; set; } = "";
        public string? ImageUrl { get; set; }
        public DateTime Timestamp { get; set; }
    }
}