using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class WhatsAppHandler
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly AutomationEngine _automationEngine;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private int? _boundPort;

        public bool IsRunning { get; private set; }
        public int LocalPort => _configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090;

        public WhatsAppHandler(
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
                var settings = _configService.Config?.WhatsApp;
                bool cloudEnabled = settings?.Enabled == true && WhatsAppModes.UsesCloudApi(settings.Mode);
                bool baileysInboundEnabled = _configService.Config?.Baileys?.Enabled == true && WhatsAppModes.UsesBaileys(settings?.Mode);
                if (settings == null || (!cloudEnabled && !baileysInboundEnabled))
                {
                    await StopCoreAsync(log: false);
                    return true;
                }

                if (IsRunning && _listener?.IsListening == true && _boundPort == LocalPort)
                {
                    return true;
                }

                await StopCoreAsync(log: false);

                string activePrefix = await StartListenerAsync();
                _cts = new CancellationTokenSource();
                _boundPort = LocalPort;
                _ = Task.Run(() => ListenAsync(_cts.Token));

                IsRunning = true;
                await _loggingService.LogInfoAsync(
                    $"WhatsApp desktop listener aktif di {activePrefix.TrimEnd('/')}/whatsapp/webhook",
                    "WhatsApp");

                if (cloudEnabled && string.IsNullOrWhiteSpace(settings.AppSecret))
                {
                    await _loggingService.LogWarningAsync(
                        "App Secret WhatsApp belum diisi. Listener tetap jalan untuk mode local/test, tetapi belum production-ready.",
                        "WhatsApp");
                }

                return true;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                _boundPort = null;
                await _loggingService.LogErrorAsync($"Gagal memulai WhatsApp handler: {ex.Message}", "WhatsApp", ex.ToString());
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
                await StopCoreAsync(log: true);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Gagal menghentikan WhatsApp handler: {ex.Message}", "WhatsApp", ex.ToString());
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task SendMessageAsync(string recipient, string message)
        {
            var record = new OutboundMessageRecord
            {
                Channel = ChannelType.WhatsApp,
                RecipientId = recipient,
                Text = message
            };

            await SendQueuedMessageAsync(record);
        }

        public async Task<string?> SendQueuedMessageAsync(OutboundMessageRecord record)
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings == null || !settings.Enabled || !WhatsAppModes.UsesCloudApi(settings.Mode))
            {
                return null;
            }

            if (!CanSendOutbound())
            {
                throw new InvalidOperationException("WhatsApp Cloud API belum siap kirim. Lengkapi Access Token dan Phone Number ID.");
            }

            string url = $"https://graph.facebook.com/{settings.GraphApiVersion ?? "v22.0"}/{settings.PhoneNumberId}/messages";
            object payload = IsTemplateRecord(record)
                ? BuildTemplatePayload(settings, record)
                : BuildTextPayload(record);

            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            await _loggingService.LogInfoAsync(
                $"WA Cloud send status={(int)response.StatusCode} {response.StatusCode}",
                "WhatsApp",
                TruncateLogDetails(body),
                record.RecipientId);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(BuildGraphApiErrorMessage((int)response.StatusCode, body));
            }

            string? externalId = TryExtractOutboundMessageId(body);
            await _loggingService.LogInfoAsync(
                $"Balasan WhatsApp {(IsTemplateRecord(record) ? "template" : "text")} dikirim ke {record.RecipientId}",
                "WhatsApp");
            return externalId;
        }

        private static string TruncateLogDetails(string? value, int maxLength = 2000)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
        }

        public bool CanSendOutbound()
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings == null || !settings.Enabled || !WhatsAppModes.UsesCloudApi(settings.Mode))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(settings.AccessToken) &&
                   !string.IsNullOrWhiteSpace(settings.PhoneNumberId);
        }

        public string BuildActionHint()
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings == null || settings.Enabled != true)
            {
                return "WhatsApp Cloud nonaktif.";
            }

            if (!WhatsAppModes.UsesCloudApi(settings.Mode))
            {
                return "WhatsApp Cloud tidak dipakai di mode saat ini.";
            }

            if (!CanSendOutbound())
            {
                return "WhatsApp Cloud belum siap kirim. Lengkapi Access Token dan Phone Number ID.";
            }

            return "WhatsApp Cloud siap kirim.";
        }

        public async Task<(bool Success, string Message)> TestCredentialsAsync()
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings == null || !settings.Enabled || !WhatsAppModes.UsesCloudApi(settings.Mode))
            {
                return (false, "Mode Cloud API belum diaktifkan.");
            }

            if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.PhoneNumberId))
            {
                return (false, "Access token atau Phone Number ID belum lengkap.");
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string url = $"https://graph.facebook.com/{settings.GraphApiVersion ?? "v22.0"}/{settings.PhoneNumberId}?fields=id";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                using var response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                return response.IsSuccessStatusCode
                    ? (true, "Meta credentials valid.")
                    : (false, BuildGraphApiErrorMessage((int)response.StatusCode, body));
            }
            catch (Exception ex)
            {
                return (false, $"Test Meta credentials gagal: {ex.Message}");
            }
        }

        public Task<(bool Success, string Message)> TestWebhookReadinessAsync(string? publicBaseUrl)
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings == null || !settings.Enabled || !WhatsAppModes.UsesCloudApi(settings.Mode))
            {
                return Task.FromResult((false, "Mode Cloud API belum diaktifkan."));
            }

            string localWebhookUrl = $"http://localhost:{LocalPort}/whatsapp/webhook";
            string? publicWebhookUrl = BuildWebhookCallbackUrl(publicBaseUrl, settings.PublicWebhookUrl);

            if (string.IsNullOrWhiteSpace(settings.VerifyToken))
            {
                return Task.FromResult((false, "Verify token belum diisi."));
            }

            string note = string.IsNullOrWhiteSpace(settings.AppSecret)
                ? "Listener siap untuk local/test, tetapi App Secret belum diisi sehingga belum production-ready."
                : "Listener dan signature validation siap untuk verifikasi Meta.";
            string callbackInfo = string.IsNullOrWhiteSpace(publicWebhookUrl)
                ? $"Local callback: {localWebhookUrl}. Untuk Meta online, isi Public Base URL atau jalankan tunnel HTTPS."
                : $"Public callback: {publicWebhookUrl}. Local callback: {localWebhookUrl}.";

            return Task.FromResult((true, $"{callbackInfo} {note}"));
        }

        public async Task<(bool Success, string Message)> TestOutboundAsync(string recipient, string message)
        {
            try
            {
                string? externalId = await SendQueuedMessageAsync(new OutboundMessageRecord
                {
                    Channel = ChannelType.WhatsApp,
                    RecipientId = recipient,
                    Text = message
                });

                return (true, $"Pesan test terkirim. Message ID: {externalId ?? "-"}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string Message)> TestTemplateOutboundAsync(string recipient, string templateKey, string message)
        {
            try
            {
                var settings = _configService.Config?.WhatsApp;
                var mapping = ResolveTemplateMapping(settings, templateKey);
                if (settings?.EnableTemplateMessages != true)
                {
                    return (false, "Template message WhatsApp masih OFF. Aktifkan hanya jika template Meta sudah approved.");
                }

                if (mapping == null)
                {
                    return (false, $"Mapping template '{templateKey}' belum diisi.");
                }

                string? externalId = await SendQueuedMessageAsync(new OutboundMessageRecord
                {
                    Channel = ChannelType.WhatsApp,
                    RecipientId = recipient,
                    Text = message,
                    MessageKind = "template",
                    TemplateName = mapping.TemplateName,
                    TemplateLanguageCode = ResolveTemplateLanguage(settings, mapping),
                    TemplateBodyParameterCount = Math.Max(0, mapping.BodyParameterCount)
                });

                return (true, $"Template test terkirim. Message ID: {externalId ?? "-"}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context), cancellationToken);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"WhatsApp listener error: {ex.Message}", "WhatsApp", ex.ToString());
                }
            }

            IsRunning = false;
        }

        private async Task<string> StartListenerAsync()
        {
            string[] preferredPrefixes =
            {
                $"http://*:{LocalPort}/",
                $"http://+:{LocalPort}/",
                $"http://localhost:{LocalPort}/",
                $"http://127.0.0.1:{LocalPort}/"
            };

            Exception? lastError = null;
            foreach (string prefix in preferredPrefixes)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);

                try
                {
                    listener.Start();
                    _listener = listener;

                    if (prefix.Contains('*') || prefix.Contains('+'))
                    {
                        await _loggingService.LogInfoAsync(
                            $"WhatsApp listener memakai wildcard prefix {prefix}. Host dari tunnel/public URL akan diterima.",
                            "WhatsApp");
                    }
                    else
                    {
                        await _loggingService.LogWarningAsync(
                            $"WhatsApp listener fallback ke {prefix}. Jika tunnel public menampilkan 'Invalid Hostname', pakai cloudflared dengan --http-host-header localhost:{LocalPort}.",
                            "WhatsApp");
                    }

                    return prefix;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    listener.Close();
                    await _loggingService.LogWarningAsync(
                        $"Prefix listener WhatsApp {prefix} tidak bisa dipakai: {ex.Message}",
                        "WhatsApp");
                }
            }

            throw new InvalidOperationException($"Tidak ada prefix listener WhatsApp yang bisa dipakai di port {LocalPort}: {lastError?.Message}");
        }

        private Task StopCoreAsync(bool log)
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _boundPort = null;
            IsRunning = false;

            return log
                ? _loggingService.LogInfoAsync("WhatsApp handler dihentikan.", "WhatsApp")
                : Task.CompletedTask;
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;

                if (context.Request.HttpMethod == "GET" && path.Equals("/whatsapp/webhook", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleVerificationAsync(context);
                    return;
                }

                if (context.Request.HttpMethod == "GET" && path.Equals("/health/integrations", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleHealthAsync(context);
                    return;
                }

                if (context.Request.HttpMethod == "POST" && path.Equals("/baileys/events/inbound", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBaileysInboundAsync(context);
                    return;
                }

                if (context.Request.HttpMethod == "POST" && path.Equals("/whatsapp/webhook", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleWebhookAsync(context);
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"WhatsApp request error: {ex.Message}", "WhatsApp", ex.ToString());
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }

        private async Task HandleVerificationAsync(HttpListenerContext context)
        {
            string mode = context.Request.QueryString["hub.mode"] ?? string.Empty;
            string verifyToken = context.Request.QueryString["hub.verify_token"] ?? string.Empty;
            string challenge = context.Request.QueryString["hub.challenge"] ?? string.Empty;
            string expectedToken = _configService.Config?.WhatsApp?.VerifyToken ?? string.Empty;

            if (mode == "subscribe" && verifyToken == expectedToken)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(challenge);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/plain";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = 403;
            context.Response.Close();
        }

        private async Task HandleWebhookAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            if (!ValidateSignature(body, context.Request.Headers["X-Hub-Signature-256"]))
            {
                await _automationEngine.RecordExternalStatusEventAsync(ChannelType.WhatsApp, null, "invalid_signature", body);
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }

            using JsonDocument document = JsonDocument.Parse(body);

            foreach (var statusEvent in ExtractStatusEvents(document.RootElement, body))
            {
                await _automationEngine.RecordExternalStatusEventAsync(
                    ChannelType.WhatsApp,
                    statusEvent.ExternalMessageId,
                    statusEvent.Status,
                    body,
                    statusEvent.CorrelationId,
                    statusEvent.ErrorDetails);

                await LogDeliveryStatusAsync(statusEvent);
            }

            foreach (var inbound in ExtractInboundMessages(document.RootElement))
            {
                inbound.PayloadHash = ComputePayloadHash(body, inbound.MessageId, inbound.SenderId);
                await _automationEngine.ProcessInboundMessageAsync(inbound);
            }

            context.Response.StatusCode = 200;
            context.Response.Close();
        }

        private async Task LogDeliveryStatusAsync(MessageStatusEventRecord statusEvent)
        {
            string recipient = string.IsNullOrWhiteSpace(statusEvent.RecipientId)
                ? "-"
                : statusEvent.RecipientId!;
            string messageId = string.IsNullOrWhiteSpace(statusEvent.ExternalMessageId)
                ? "-"
                : statusEvent.ExternalMessageId!;
            string details = string.IsNullOrWhiteSpace(statusEvent.ErrorDetails)
                ? $"message_id={messageId}"
                : $"message_id={messageId}; error={statusEvent.ErrorDetails}";

            if (string.Equals(statusEvent.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                await _loggingService.LogWarningAsync(
                    $"WA delivery status=failed recipient={recipient}",
                    "WhatsApp",
                    details,
                    recipient);
                return;
            }

            await _loggingService.LogInfoAsync(
                $"WA delivery status={statusEvent.Status} recipient={recipient}",
                "WhatsApp",
                details,
                recipient);
        }

        private async Task HandleHealthAsync(HttpListenerContext context)
        {
            var integration = _automationEngine.GetIntegrationStatus(
                telegramRunning: false,
                whatsAppRunning: IsRunning,
                tunnelRunning: false,
                tunnelPublicUrl: _configService.Config?.WhatsApp?.PublicWebhookUrl);

            var payload = JsonSerializer.Serialize(new
            {
                whatsappRunning = IsRunning,
                localPort = LocalPort,
                mode = WhatsAppModes.Normalize(_configService.Config?.WhatsApp?.Mode),
                publicWebhookUrl = integration.WhatsAppWebhookUrl,
                pendingOutboundCount = integration.PendingOutboundCount,
                lastWebhookReceivedAt = integration.LastWebhookReceivedAt,
                lastWebhookStatus = integration.LastWebhookStatus,
                lastOutboundSentAt = integration.LastOutboundSentAt,
                lastOutboundFailureAt = integration.LastOutboundFailureAt,
                productionReady = integration.ProductionReady
            });

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private bool ValidateSignature(string body, string? signatureHeader)
        {
            string? appSecret = _configService.Config?.WhatsApp?.AppSecret;
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            string expectedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            string actualHash = signatureHeader["sha256=".Length..].Trim().ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHash),
                Encoding.UTF8.GetBytes(actualHash));
        }

        private static bool IsTemplateRecord(OutboundMessageRecord record)
        {
            return string.Equals(record.MessageKind, "template", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrWhiteSpace(record.TemplateName);
        }

        private static object BuildTextPayload(OutboundMessageRecord record)
        {
            return new
            {
                messaging_product = "whatsapp",
                to = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId),
                type = "text",
                text = new
                {
                    preview_url = false,
                    body = record.Text
                }
            };
        }

        private static object BuildTemplatePayload(WhatsAppSettings settings, OutboundMessageRecord record)
        {
            if (settings.EnableTemplateMessages != true)
            {
                throw new InvalidOperationException("Template message WhatsApp masih OFF. Pesan proaktif Cloud API tidak dikirim untuk menghindari biaya/penolakan API.");
            }

            if (string.IsNullOrWhiteSpace(record.TemplateName))
            {
                throw new InvalidOperationException("Template WhatsApp belum dipilih untuk pesan ini.");
            }

            int bodyParameterCount = Math.Max(0, record.TemplateBodyParameterCount);
            var components = new List<object>();
            if (bodyParameterCount > 0)
            {
                var parameters = Enumerable.Range(0, bodyParameterCount)
                    .Select(index => new
                    {
                        type = "text",
                        text = index == 0 ? record.Text : "-"
                    })
                    .ToArray();

                components.Add(new
                {
                    type = "body",
                    parameters
                });
            }

            return new
            {
                messaging_product = "whatsapp",
                to = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId),
                type = "template",
                template = new
                {
                    name = record.TemplateName,
                    language = new
                    {
                        code = string.IsNullOrWhiteSpace(record.TemplateLanguageCode)
                            ? settings.DefaultTemplateLanguageCode ?? "id"
                            : record.TemplateLanguageCode
                    },
                    components = components.ToArray()
                }
            };
        }

        private async Task HandleBaileysInboundAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            var payload = JsonSerializer.Deserialize<BaileysInboundPayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload == null || string.IsNullOrWhiteSpace(payload.SenderId))
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            var inbound = new InboundMessage
            {
                Channel = ChannelType.Baileys,
                SenderId = payload.SenderId,
                SenderName = payload.SenderName,
                Text = payload.Text ?? payload.Caption ?? string.Empty,
                MediaUrl = payload.MediaUrl,
                MessageId = payload.MessageId,
                CorrelationId = payload.CorrelationId,
                Timestamp = payload.Timestamp ?? DateTime.Now
            };

            inbound.PayloadHash = ComputePayloadHash(body, inbound.MessageId, inbound.SenderId);
            await _loggingService.LogInfoAsync(
                $"Baileys inbound diterima dari {inbound.SenderId}: {inbound.Text}",
                "WhatsApp",
                $"message_id={inbound.MessageId ?? "-"}; raw_jid={payload.RawSenderJid ?? "-"}; resolved_jid={payload.ResolvedSenderJid ?? "-"}",
                inbound.SenderId);
            await _automationEngine.ProcessInboundMessageAsync(inbound);

            context.Response.StatusCode = 200;
            context.Response.Close();
        }

        private IEnumerable<InboundMessage> ExtractInboundMessages(JsonElement root)
        {
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    string senderName = "";
                    if (value.TryGetProperty("contacts", out var contacts) &&
                        contacts.ValueKind == JsonValueKind.Array &&
                        contacts.GetArrayLength() > 0 &&
                        contacts[0].TryGetProperty("profile", out var profile) &&
                        profile.TryGetProperty("name", out var nameProp))
                    {
                        senderName = nameProp.GetString() ?? "";
                    }

                    if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var message in messages.EnumerateArray())
                    {
                        string text = message.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("body", out var bodyProp)
                            ? bodyProp.GetString() ?? ""
                            : message.TryGetProperty("image", out var imageObj) && imageObj.TryGetProperty("caption", out var captionProp)
                                ? captionProp.GetString() ?? "Pesan gambar diterima."
                                : "Pesan non-teks diterima.";

                        yield return new InboundMessage
                        {
                            Channel = ChannelType.WhatsApp,
                            SenderId = message.GetProperty("from").GetString() ?? "",
                            SenderName = senderName,
                            Text = text,
                            MediaUrl = message.TryGetProperty("image", out var imageData) && imageData.TryGetProperty("id", out var mediaId)
                                ? mediaId.GetString()
                                : null,
                            MessageId = message.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                            Timestamp = message.TryGetProperty("timestamp", out var tsProp) &&
                                        long.TryParse(tsProp.GetString(), out var unixTs)
                                ? DateTimeOffset.FromUnixTimeSeconds(unixTs).LocalDateTime
                                : DateTime.Now
                        };
                    }
                }
            }
        }

        private IEnumerable<MessageStatusEventRecord> ExtractStatusEvents(JsonElement root, string rawPayload)
        {
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var status in statuses.EnumerateArray())
                    {
                        yield return new MessageStatusEventRecord
                        {
                            Channel = ChannelType.WhatsApp.ToString(),
                            ExternalMessageId = status.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                            RecipientId = status.TryGetProperty("recipient_id", out var recipientProp) ? recipientProp.GetString() : null,
                            Status = status.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "status_update" : "status_update",
                            ErrorDetails = ExtractStatusErrorDetails(status),
                            RawPayload = rawPayload,
                            RecordedAt = DateTime.Now
                        };
                    }
                }
            }
        }

        private static string? ExtractStatusErrorDetails(JsonElement status)
        {
            if (!status.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array ||
                errors.GetArrayLength() == 0)
            {
                return null;
            }

            var details = new List<string>();
            foreach (var error in errors.EnumerateArray())
            {
                string code = error.TryGetProperty("code", out var codeProp)
                    ? codeProp.ToString()
                    : "";
                string title = error.TryGetProperty("title", out var titleProp)
                    ? titleProp.GetString() ?? ""
                    : "";
                string message = error.TryGetProperty("message", out var messageProp)
                    ? messageProp.GetString() ?? ""
                    : "";
                string errorData = "";
                if (error.TryGetProperty("error_data", out var errorDataProp) &&
                    errorDataProp.TryGetProperty("details", out var dataDetailsProp))
                {
                    errorData = dataDetailsProp.GetString() ?? "";
                }

                string line = string.Join(" - ", new[] { code, title, message, errorData }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    details.Add(line);
                }
            }

            string result = string.Join("; ", details);
            return string.IsNullOrWhiteSpace(result)
                ? null
                : result.Length <= 1000 ? result : result[..1000];
        }

        private static WhatsAppTemplateMapping? ResolveTemplateMapping(WhatsAppSettings? settings, string templateKey)
        {
            return settings?.TemplateMappings?
                .FirstOrDefault(mapping =>
                    string.Equals(mapping.Key, templateKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(mapping.TemplateName));
        }

        private static string ResolveTemplateLanguage(WhatsAppSettings? settings, WhatsAppTemplateMapping mapping)
        {
            return string.IsNullOrWhiteSpace(mapping.LanguageCode)
                ? settings?.DefaultTemplateLanguageCode ?? "id"
                : mapping.LanguageCode!;
        }

        private static string? BuildWebhookCallbackUrl(params string?[] candidates)
        {
            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string value = candidate.Trim().TrimEnd('/');
                if (value.EndsWith("/whatsapp/webhook", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }

                return $"{value}/whatsapp/webhook";
            }

            return null;
        }

        private static string? TryExtractOutboundMessageId(string responseBody)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("messages", out var messages) &&
                    messages.ValueKind == JsonValueKind.Array &&
                    messages.GetArrayLength() > 0 &&
                    messages[0].TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
            catch
            {
            }

            return null;
        }

        private static string BuildGraphApiErrorMessage(int statusCode, string responseBody)
        {
            string? graphMessage = null;
            int? code = null;
            int? subcode = null;
            string? fbtraceId = null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    graphMessage = error.TryGetProperty("message", out var messageProp) ? messageProp.GetString() : null;
                    code = error.TryGetProperty("code", out var codeProp) && codeProp.TryGetInt32(out int parsedCode) ? parsedCode : null;
                    subcode = error.TryGetProperty("error_subcode", out var subcodeProp) && subcodeProp.TryGetInt32(out int parsedSubcode) ? parsedSubcode : null;
                    fbtraceId = error.TryGetProperty("fbtrace_id", out var traceProp) ? traceProp.GetString() : null;
                }
            }
            catch
            {
            }

            string actionHint;
            if (statusCode == 401 || code == 190)
            {
                actionHint = "Token Meta invalid/expired atau bukan token untuk app/WABA ini.";
            }
            else if (statusCode == 403)
            {
                actionHint = "Token tidak punya permission atau Phone Number ID tidak sesuai dengan WABA/token.";
            }
            else if (statusCode == 429 || code == 4 || code == 17)
            {
                actionHint = "Rate limit Meta tercapai. Coba lagi setelah jeda.";
            }
            else if ((graphMessage ?? string.Empty).Contains("outside", StringComparison.OrdinalIgnoreCase) ||
                     (graphMessage ?? string.Empty).Contains("24", StringComparison.OrdinalIgnoreCase) ||
                     code == 131047)
            {
                actionHint = "Pesan text ditolak karena di luar 24-hour customer service window. Aktifkan dan gunakan template WhatsApp approved.";
            }
            else if (statusCode == 400)
            {
                actionHint = "Request ditolak Meta. Periksa nomor tujuan, template, bahasa template, dan parameter.";
            }
            else
            {
                actionHint = "Graph API mengembalikan error.";
            }

            string detail = string.IsNullOrWhiteSpace(graphMessage) ? responseBody : graphMessage!;
            string trace = string.IsNullOrWhiteSpace(fbtraceId) ? "" : $" fbtrace_id={fbtraceId}.";
            string subcodeText = subcode.HasValue ? $" subcode={subcode.Value}." : "";
            string codeText = code.HasValue ? $" code={code.Value}." : "";
            return $"Graph API {statusCode}: {actionHint} Detail: {detail}.{codeText}{subcodeText}{trace}";
        }

        private static string ComputePayloadHash(string body, string? messageId, string senderId)
        {
            string source = $"{messageId}|{senderId}|{body}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private sealed class BaileysInboundPayload
        {
            public string SenderId { get; set; } = "";
            public string? SenderName { get; set; }
            public string? Text { get; set; }
            public string? Caption { get; set; }
            public string? MediaUrl { get; set; }
            public string? MessageId { get; set; }
            public string? CorrelationId { get; set; }
            public DateTime? Timestamp { get; set; }
            public string? RawSenderJid { get; set; }
            public string? ResolvedSenderJid { get; set; }
        }
    }
}
