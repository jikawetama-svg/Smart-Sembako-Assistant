using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class BaileysSidecarService
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly HttpClient _httpClient;
        private Process? _process;
        private CancellationTokenSource? _watchdogCts;
        private DateTime? _disconnectedSince;
        private int _watchdogTriggerCount;
        private int _lastDesktopInboundPort = 8090;
        private bool _watchdogRestarting;

        public bool IsRunning { get; private set; }
        public bool IsReachable { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsPaired { get; private set; }
        public string? LastError { get; private set; }
        public string? LastPairingCode { get; private set; }
        public string? ConnectionState { get; private set; }
        public int? LastDisconnectStatusCode { get; private set; }
        public string? LastDisconnectReason { get; private set; }
        public string? SidecarBuildTag { get; private set; }
        public bool PairingInProgress { get; private set; }
        public DateTime? LastValidatedAt { get; private set; }
        public int LocalApiPort => _configService.Config?.Baileys?.LocalApiPort ?? 8091;
        public string BaseUrl => $"http://127.0.0.1:{LocalApiPort}";

        public BaileysSidecarService(
            ConfigService configService,
            LoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async Task<bool> StartAsync(int desktopInboundPort)
        {
            _lastDesktopInboundPort = desktopInboundPort;
            LastValidatedAt = DateTime.Now;
            var config = _configService.Config;
            string mode = WhatsAppModes.Normalize(config?.WhatsApp?.Mode);
            var baileys = config?.Baileys;

            if (baileys == null || !baileys.Enabled || !WhatsAppModes.UsesBaileys(mode))
            {
                IsRunning = false;
                IsReachable = false;
                IsPaired = false;
                PairingInProgress = false;
                return true;
            }

            string? nodeBinary = ResolveNodeBinaryPath(baileys.NodeBinaryPath);
            string? workingDirectory = ResolveWorkingDirectory(baileys);
            string? sidecarEntry = ResolveSidecarEntryPath(baileys, workingDirectory);
            string sessionPath = RuntimePaths.ResolveWritablePath(baileys.SessionPath, Path.Combine("data", "baileys-session"));
            string mediaPath = RuntimePaths.ResolveWritablePath(Path.Combine("data", "baileys-media"), Path.Combine("data", "baileys-media"));

            await _loggingService.LogInfoAsync(
                $"Baileys runtime paths: node={nodeBinary ?? "-"}, sidecar={sidecarEntry ?? "-"}, workingDir={workingDirectory ?? "-"}, session={sessionPath}",
                "Baileys");

            if (string.IsNullOrWhiteSpace(nodeBinary))
            {
                LastError = "Node.js runtime tidak ditemukan. Paket installer harus menyertakan runtimes\\node\\node.exe atau Node.js tersedia di PATH.";
                await _loggingService.LogWarningAsync(LastError, "Baileys");
                IsReachable = false;
                IsRunning = false;
                return false;
            }

            if (string.IsNullOrWhiteSpace(sidecarEntry) || !File.Exists(sidecarEntry))
            {
                LastError = "File sidecar WhatsApp lokal tidak ditemukan. Buka Settings lanjutan dan periksa path sidecar.";
                await _loggingService.LogWarningAsync(LastError, "Baileys");
                IsReachable = false;
                IsRunning = false;
                return false;
            }

            if (!await EnsureDependenciesAsync())
            {
                IsReachable = false;
                IsRunning = false;
                return false;
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    await RefreshHealthAsync();
                    IsRunning = IsReachable || !_process.HasExited;
                    if (IsRunning)
                    {
                        StartWatchdog();
                    }
                    return IsRunning;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = nodeBinary,
                    Arguments = $"\"{sidecarEntry}\"",
                    WorkingDirectory = workingDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.Environment["SSA_LOCAL_API_PORT"] = baileys.LocalApiPort.ToString();
                startInfo.Environment["SSA_SESSION_PATH"] = sessionPath;
                startInfo.Environment["SSA_MEDIA_PATH"] = mediaPath;
                startInfo.Environment["SSA_DESKTOP_INBOUND_URL"] = $"http://localhost:{desktopInboundPort}/baileys/events/inbound";
                startInfo.Environment["SSA_PAIRING_CODE_TTL_SECONDS"] = Math.Max(30, baileys.PairingCodeTtlSeconds).ToString();
                startInfo.Environment["SSA_PAIRING_RETRY_COOLDOWN_SECONDS"] = Math.Max(15, baileys.PairingRetryCooldownSeconds).ToString();
                startInfo.Environment["SSA_PAIRING_RATE_LIMIT_COOLDOWN_MINUTES"] = Math.Max(1, baileys.PairingRateLimitCooldownMinutes).ToString();
                startInfo.Environment["SSA_MAX_PAIRING_REQUESTS_PER_HOUR"] = Math.Max(1, baileys.MaxPairingRequestsPerHour).ToString();
                startInfo.Environment["SSA_INBOUND_STALE_TOLERANCE_SECONDS"] = "120";
                startInfo.Environment["SSA_AUTHORIZED_NUMBERS"] = BuildAuthorizedNumbersEnv(baileys);
                startInfo.Environment["SSA_APP_INSTANCE_ID"] = _configService.Config?.App?.InstanceId ?? string.Empty;
                startInfo.Environment["SSA_MACHINE_NAME"] = _configService.Config?.App?.MachineName ?? Environment.MachineName;

                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += HandleProcessOutput;
                _process.ErrorDataReceived += HandleProcessOutput;
                _process.Exited += async (_, _) =>
                {
                    IsRunning = false;
                    IsReachable = false;
                    ConnectionState = "closed";
                    await _loggingService.LogWarningAsync("WhatsApp lokal berhenti. Anda bisa mencoba hubungkan ulang dari wizard.", "Baileys");
                };

                if (!_process.Start())
                {
                    LastError = "WhatsApp lokal gagal dijalankan.";
                    return false;
                }

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    await Task.Delay(1000);
                    await RefreshHealthAsync();
                    if (IsReachable)
                    {
                        IsRunning = true;
                        StartWatchdog();
                        await _loggingService.LogInfoAsync($"Baileys sidecar aktif di {BaseUrl}; build={SidecarBuildTag ?? "-"}", "Baileys");
                        return true;
                    }
                }

                LastError = "WhatsApp lokal sedang dijalankan tetapi belum merespons. Coba lagi atau perbaiki setup otomatis.";
                IsRunning = _process != null && !_process.HasExited;
                return IsRunning;
            }
            catch (Exception ex)
            {
                LastError = ToFriendlyError(ex.Message);
                IsRunning = false;
                IsReachable = false;
                await _loggingService.LogErrorAsync($"Gagal memulai Baileys sidecar: {ex.Message}", "Baileys", ex.ToString());
                return false;
            }
        }

        public async Task<bool> EnsureStartedAsync(int desktopInboundPort)
        {
            await RefreshHealthAsync();
            if (IsReachable)
            {
                IsRunning = true;
                return true;
            }

            return await StartAsync(desktopInboundPort);
        }

        public async Task<bool> EnsureDependenciesAsync()
        {
            var settings = _configService.Config?.Baileys;
            if (settings == null)
            {
                LastError = "Konfigurasi Baileys belum ada.";
                return false;
            }

            string? workingDirectory = ResolveWorkingDirectory(settings);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                LastError = "Folder sidecar WhatsApp lokal tidak ditemukan.";
                return false;
            }

            string packageJson = Path.Combine(workingDirectory, "package.json");
            if (!File.Exists(packageJson))
            {
                LastError = "package.json sidecar tidak ditemukan.";
                return false;
            }

            string nodeModules = Path.Combine(workingDirectory, "node_modules");
            bool hasRequiredModules =
                File.Exists(Path.Combine(nodeModules, "pino", "package.json")) &&
                File.Exists(Path.Combine(nodeModules, "@whiskeysockets", "baileys", "package.json"));

            if (Directory.Exists(nodeModules) && hasRequiredModules)
            {
                return true;
            }

            if (!IsDevelopmentSidecarDirectory(workingDirectory))
            {
                LastError = "Dependency WhatsApp lokal tidak lengkap di folder install. Paket installer harus menyertakan Integrations\\BaileysSidecar\\node_modules.";
                return false;
            }

            if (Directory.Exists(nodeModules) && !hasRequiredModules)
            {
                try
                {
                    Directory.Delete(nodeModules, recursive: true);
                }
                catch
                {
                    // Lanjut ke npm install; folder akan ditimpa jika memungkinkan.
                }
            }

            string? npmBinary = ResolveNpmBinaryPath();
            if (string.IsNullOrWhiteSpace(npmBinary))
            {
                LastError = "npm tidak ditemukan untuk mode development. Jalankan npm install manual di Integrations\\BaileysSidecar atau pakai installer lengkap.";
                return false;
            }

            try
            {
                var install = new ProcessStartInfo
                {
                    FileName = npmBinary,
                    Arguments = "install",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(install);
                if (process == null)
                {
                    LastError = "Gagal menjalankan npm install untuk sidecar WhatsApp lokal.";
                    return false;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    LastError = $"Dependency WhatsApp lokal gagal dipasang: {TrimMessage(stderr, stdout)}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = ToFriendlyError(ex.Message);
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _watchdogCts?.Cancel();
                _watchdogCts?.Dispose();
                _watchdogCts = null;

                if (_process != null && !_process.HasExited)
                {
                    await RequestGracefulShutdownAsync();
                    if (!_process.HasExited)
                    {
                        await Task.Delay(1000);
                    }

                    if (!_process.HasExited)
                    {
                        _process.Kill(true);
                    }

                    await _process.WaitForExitAsync();
                }

                IsRunning = false;
                IsReachable = false;
                IsConnected = false;
                PairingInProgress = false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Gagal menghentikan Baileys sidecar: {ex.Message}", "Baileys", ex.ToString());
            }
        }

        public bool CanSendOutbound()
        {
            return IsReachable && IsPaired && IsConnected;
        }

        public async Task<string?> SendQueuedMessageAsync(OutboundMessageRecord record)
        {
            await RefreshHealthAsync();
            if (!CanSendOutbound())
            {
                await TryRecoverOutboundAsync();
            }

            if (!CanSendOutbound())
            {
                throw new InvalidOperationException(BuildActionHint());
            }

            var response = await PostJsonAsync(
                "/messages/send",
                new
                {
                    recipient = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId),
                    text = record.Text,
                    correlationId = record.CorrelationId
                });

            if (!response.Success && ShouldRecoverOutbound(response))
            {
                await TryRecoverOutboundAsync();
                if (CanSendOutbound())
                {
                    response = await PostJsonAsync(
                        "/messages/send",
                        new
                        {
                            recipient = AutomationEngine.NormalizeWhatsAppNumber(record.RecipientId),
                            text = record.Text,
                            correlationId = record.CorrelationId
                        });
                }
            }

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Message);
            }

            return response.ExternalMessageId;
        }

        public async Task SendTypingPresenceAsync(string recipientId, bool paused = false)
        {
            await RefreshHealthAsync();
            if (!IsReachable || !IsConnected)
            {
                return;
            }

            await PostJsonAsync(
                "/presence/typing",
                new
                {
                    recipient = AutomationEngine.NormalizeWhatsAppNumber(recipientId),
                    paused
                });
        }

        public async Task<string?> SendDocumentAsync(string recipientId, string filePath, string caption = "")
        {
            await RefreshHealthAsync();
            if (!CanSendOutbound())
            {
                await TryRecoverOutboundAsync();
            }

            if (!CanSendOutbound())
            {
                throw new InvalidOperationException(BuildActionHint());
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("File dokumen export tidak ditemukan.", filePath);
            }

            var fileInfo = new FileInfo(filePath);
            var response = await PostJsonAsync(
                "/messages/send-document",
                new
                {
                    recipient = AutomationEngine.NormalizeWhatsAppNumber(recipientId),
                    filePath = fileInfo.FullName,
                    fileName = fileInfo.Name,
                    mimeType = ResolveMimeType(fileInfo.Extension),
                    caption
                });

            if (!response.Success && ShouldRecoverOutbound(response))
            {
                await TryRecoverOutboundAsync();
                if (CanSendOutbound())
                {
                    response = await PostJsonAsync(
                        "/messages/send-document",
                        new
                        {
                            recipient = AutomationEngine.NormalizeWhatsAppNumber(recipientId),
                            filePath = fileInfo.FullName,
                            fileName = fileInfo.Name,
                            mimeType = ResolveMimeType(fileInfo.Extension),
                            caption
                        });
                }
            }

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Message);
            }

            await _loggingService.LogInfoAsync(
                $"Dokumen Baileys terkirim: {fileInfo.Name} ({fileInfo.Length} bytes) ke {AutomationEngine.NormalizeWhatsAppNumber(recipientId)}",
                "Baileys");
            return response.ExternalMessageId;
        }

        public async Task<(bool Success, string Message)> TestHealthAsync()
        {
            bool started = await EnsureStartedAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090);
            await RefreshHealthAsync();

            if (started && IsReachable)
            {
                var status = await GetSessionStatusAsync();
                string state = status?.Paired == true
                    ? "WhatsApp sudah terhubung."
                    : "WhatsApp lokal sudah jalan, tetapi masih menunggu pairing.";
                return (true, state);
            }

            return (false, LastError ?? "WhatsApp lokal belum siap.");
        }

        public async Task<BaileysSessionStatus?> GetSessionStatusAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync($"{BaseUrl}/session/status");
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    LastError = ToFriendlyError(content);
                    return null;
                }

                var status = JsonSerializer.Deserialize<BaileysSessionStatus>(content, JsonOptions());
                if (status != null)
                {
                    ApplyStatusSnapshot(status);
                }
                return status;
            }
            catch (Exception ex)
            {
                LastError = ToFriendlyError(ex.Message);
                return null;
            }
        }

        public async Task<BaileysPairingResult> StartPairingAsync(string? phoneNumber)
        {
            return await GeneratePairingCodeAsync(phoneNumber);
        }

        public async Task<BaileysQrPairingResult> StartQrPairingAsync(bool resetSessionFirst = true)
        {
            if (!await EnsureStartedAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090))
            {
                return new BaileysQrPairingResult
                {
                    Success = false,
                    Message = LastError ?? "WhatsApp lokal belum siap.",
                    Reason = "not-ready",
                    RetryAfterSeconds = _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30
                };
            }

            var response = await PostJsonAsync("/session/qr/start", new { resetSession = resetSessionFirst });
            if (!response.Success)
            {
                LastError = BuildPairingFailureMessage(response);
                return new BaileysQrPairingResult
                {
                    Success = false,
                    Message = LastError,
                    Reason = response.Reason,
                    RetryAfterSeconds = response.RetryAfterSeconds ?? GetFallbackPairingRetryAfterSeconds(response.Reason)
                };
            }

            await _loggingService.LogInfoAsync(
                $"QR pairing requested: available={response.QrAvailable}, expiresAt={response.QrCodeExpiresAt?.ToString("O") ?? "-"}, connection={ConnectionState ?? "-"}, disconnect={LastDisconnectStatusCode?.ToString() ?? "-"} {LastDisconnectReason ?? "-"}",
                "Baileys");

            return new BaileysQrPairingResult
            {
                Success = true,
                Message = response.Message,
                QrAvailable = response.QrAvailable,
                QrDataUrl = response.QrDataUrl,
                ExpiresAt = response.QrCodeExpiresAt,
                RetryAfterSeconds = response.RetryAfterSeconds,
                Reason = response.Reason
            };
        }

        public async Task<BaileysPairingResult> GeneratePairingCodeAsync(string? botPhoneNumber)
        {
            string normalized = AutomationEngine.NormalizeWhatsAppNumber(botPhoneNumber);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                LastError = "Nomor bot WhatsApp belum diisi.";
                return new BaileysPairingResult { Success = false, Message = LastError, Reason = "invalid-phone" };
            }

            if (!await EnsureStartedAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090))
            {
                return new BaileysPairingResult
                {
                    Success = false,
                    Message = LastError ?? "WhatsApp lokal belum siap.",
                    Reason = "not-ready",
                    RetryAfterSeconds = _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30
                };
            }

            PairingInProgress = true;
            try
            {
                var response = await RequestPairingCodeOnceAsync(normalized);
                if (!response.Success)
                {
                    LastError = BuildPairingFailureMessage(response);
                    return new BaileysPairingResult
                    {
                        Success = false,
                        Message = LastError,
                        Reason = response.Reason,
                        RetryAfterSeconds = response.RetryAfterSeconds ?? GetFallbackPairingRetryAfterSeconds(response.Reason)
                    };
                }

                LastPairingCode = response.PairingCode;
                await RefreshHealthAsync();

                if (string.IsNullOrWhiteSpace(LastPairingCode))
                {
                    if (IsPaired)
                    {
                        return new BaileysPairingResult
                        {
                            Success = true,
                            Message = "WhatsApp sudah terhubung.",
                            PairingCode = null,
                            ExpiresAt = response.PairingCodeExpiresAt,
                            RetryAfterSeconds = response.RetryAfterSeconds
                        };
                    }

                    LastError = "Kode pairing gagal dibuat. Tunggu cooldown lalu coba generate kode baru.";
                    return new BaileysPairingResult
                    {
                        Success = false,
                        Message = LastError,
                        Reason = "no-code",
                        RetryAfterSeconds = response.RetryAfterSeconds ?? _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30
                    };
                }

                await _loggingService.LogInfoAsync(
                    $"Pairing code received for {normalized}: raw={LastPairingCode}, formatted={FormatPairingCodeForLog(LastPairingCode)}, expiresAt={response.PairingCodeExpiresAt?.ToString("O") ?? "-"}, retryAfter={response.RetryAfterSeconds?.ToString() ?? "0"}, connection={ConnectionState ?? "-"}, disconnect={LastDisconnectStatusCode?.ToString() ?? "-"} {LastDisconnectReason ?? "-"}",
                    "Baileys");

                return new BaileysPairingResult
                {
                    Success = true,
                    Message = $"Kode pairing siap: {LastPairingCode}",
                    PairingCode = LastPairingCode,
                    ExpiresAt = response.PairingCodeExpiresAt,
                    RetryAfterSeconds = response.RetryAfterSeconds,
                    Reason = response.Reason
                };
            }
            finally
            {
                PairingInProgress = false;
            }
        }

        public async Task<BaileysPairingResult> ResetSessionAsync()
        {
            return await ResetSessionCoreAsync(ensureStarted: true);
        }

        private async Task<BaileysPairingResult> ResetSessionCoreAsync(bool ensureStarted)
        {
            if (ensureStarted && !await EnsureStartedAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090))
            {
                return new BaileysPairingResult
                {
                    Success = false,
                    Message = LastError ?? "WhatsApp lokal belum siap.",
                    Reason = "not-ready",
                    RetryAfterSeconds = _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30
                };
            }

            var response = await PostJsonAsync("/session/reset", new { });
            if (!response.Success)
            {
                return new BaileysPairingResult
                {
                    Success = false,
                    Message = BuildPairingFailureMessage(response),
                    Reason = response.Reason,
                    RetryAfterSeconds = response.RetryAfterSeconds ?? GetFallbackPairingRetryAfterSeconds(response.Reason)
                };
            }

            LastPairingCode = null;
            IsPaired = false;
            PairingInProgress = false;
            await RefreshHealthAsync();
            int retryAfter = Math.Max(15, _configService.Config?.Baileys?.PairingRetryCooldownSeconds ?? 30);
            return new BaileysPairingResult
            {
                Success = true,
                Message = $"Sesi WhatsApp lokal direset. Tunggu {retryAfter} detik sebelum generate kode baru.",
                Reason = "reset",
                RetryAfterSeconds = retryAfter
            };
        }

        private async Task RestartSidecarAsync()
        {
            await StopAsync();
            await Task.Delay(750);
            await EnsureStartedAsync(_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090);
        }

        private void StartWatchdog()
        {
            if (_watchdogCts != null && !_watchdogCts.IsCancellationRequested)
            {
                return;
            }

            _watchdogCts = new CancellationTokenSource();
            _ = Task.Run(() => RunWatchdogAsync(_watchdogCts.Token), _watchdogCts.Token);
        }

        private async Task RunWatchdogAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                    await RefreshHealthAsync();

                    if (cancellationToken.IsCancellationRequested || !IsRunning)
                    {
                        continue;
                    }

                    if (!IsReachable)
                    {
                        _watchdogTriggerCount++;
                        await _loggingService.LogWarningAsync(
                            $"Baileys watchdog: sidecar tidak merespons, restart proses. trigger={_watchdogTriggerCount}",
                            "Baileys");
                        await RestartSidecarFromWatchdogAsync(cancellationToken);
                        continue;
                    }

                    if (IsPaired && !IsConnected && _disconnectedSince.HasValue &&
                        DateTime.Now - _disconnectedSince.Value > TimeSpan.FromMinutes(5))
                    {
                        _watchdogTriggerCount++;
                        await _loggingService.LogWarningAsync(
                            $"Baileys watchdog: paired tapi disconnected lebih dari 5 menit, trigger reconnect. trigger={_watchdogTriggerCount}",
                            "Baileys");
                        await PostJsonAsync("/session/reconnect", new { });
                        _disconnectedSince = DateTime.Now;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"Baileys watchdog error: {ex.Message}", "Baileys");
                }
            }
        }

        private async Task RestartSidecarFromWatchdogAsync(CancellationToken cancellationToken)
        {
            if (_watchdogRestarting)
            {
                return;
            }

            _watchdogRestarting = true;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    await RequestGracefulShutdownAsync();
                    await Task.Delay(1000, cancellationToken);
                    if (!_process.HasExited)
                    {
                        _process.Kill(true);
                    }

                    await _process.WaitForExitAsync(cancellationToken);
                }

                _process = null;
                IsRunning = false;
                IsReachable = false;
                IsConnected = false;
                await StartAsync(_lastDesktopInboundPort);
            }
            finally
            {
                _watchdogRestarting = false;
            }
        }

        private async Task<BaileysSendResponse> RequestPairingCodeOnceAsync(string normalizedPhoneNumber)
        {
            return await PostJsonAsync("/session/pairing/start", new
            {
                phoneNumber = normalizedPhoneNumber
            });
        }

        private static bool ShouldRetryPairing(BaileysSendResponse response)
        {
            return string.Equals(response.Reason, "not-ready", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response.Reason, "connection-closed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response.Reason, "upstream-failure", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldResetSession(BaileysSendResponse response)
        {
            return string.Equals(response.Reason, "logged-out", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response.Reason, "connection-closed", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildPairingFailureMessage(BaileysSendResponse response)
        {
            if (string.Equals(response.Reason, "logged-out", StringComparison.OrdinalIgnoreCase))
            {
                return "Sesi perlu direset lalu generate pairing code ulang.";
            }

            if (string.Equals(response.Reason, "rate-limited", StringComparison.OrdinalIgnoreCase))
            {
                int seconds = response.RetryAfterSeconds ?? GetFallbackPairingRetryAfterSeconds(response.Reason);
                return $"Permintaan pairing terlalu sering. Tunggu {FormatDuration(seconds)} lalu coba lagi.";
            }

            if (string.Equals(response.Reason, "not-ready", StringComparison.OrdinalIgnoreCase))
            {
                return "WhatsApp lokal belum siap untuk pairing. Tunggu sebentar lalu coba lagi.";
            }

            if (string.Equals(response.Reason, "connection-closed", StringComparison.OrdinalIgnoreCase))
            {
                return "Koneksi WhatsApp lokal sedang reconnect. Jangan reset sesi dulu. Tunggu sampai tombol Generate Kode Baru aktif kembali.";
            }

            if (string.Equals(response.Reason, "upstream-failure", StringComparison.OrdinalIgnoreCase))
            {
                return "Baileys sedang bermasalah di upstream. Coba lagi beberapa menit lagi.";
            }

            if (string.Equals(response.Reason, "pairing-in-progress", StringComparison.OrdinalIgnoreCase))
            {
                return "Kode pairing sedang dibuat. Tunggu beberapa detik.";
            }

            return ToFriendlyError(response.Message);
        }

        private int GetFallbackPairingRetryAfterSeconds(string? reason)
        {
            var settings = _configService.Config?.Baileys;
            if (string.Equals(reason, "rate-limited", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(1, settings?.PairingRateLimitCooldownMinutes ?? 2) * 60;
            }

            return Math.Max(15, settings?.PairingRetryCooldownSeconds ?? 30);
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
            {
                return "sebentar";
            }

            int minutes = seconds / 60;
            int remainder = seconds % 60;
            return minutes > 0
                ? $"{minutes} menit {remainder:D2} detik"
                : $"{seconds} detik";
        }

        public async Task RefreshHealthAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync($"{BaseUrl}/health");
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    IsReachable = false;
                    IsConnected = false;
                    IsPaired = false;
                    LastError = ToFriendlyError(content);
                    return;
                }

                var health = JsonSerializer.Deserialize<BaileysHealthResponse>(content, JsonOptions());
                if (health != null)
                {
                    ApplyHealthSnapshot(health);
                }
            }
            catch (Exception ex)
            {
                IsReachable = false;
                IsConnected = false;
                IsPaired = false;
                LastError = ToFriendlyError(ex.Message);
            }
        }

        private async Task RequestGracefulShutdownAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                string json = JsonSerializer.Serialize(new { }, JsonOptions());
                using var response = await _httpClient.PostAsync(
                    $"{BaseUrl}/shutdown",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    timeout.Token);
            }
            catch
            {
                // Jika sidecar tidak merespons shutdown, StopAsync akan fallback ke Kill.
            }
        }

        private async Task<BaileysSendResponse> PostJsonAsync(string path, object payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload, JsonOptions());
                using var response = await _httpClient.PostAsync(
                    $"{BaseUrl}{path}",
                    new StringContent(json, Encoding.UTF8, "application/json"));

                string content = await response.Content.ReadAsStringAsync();
                var payloadResponse = JsonSerializer.Deserialize<BaileysSendResponse>(content, JsonOptions());
                if (payloadResponse != null)
                {
                    ConnectionState = payloadResponse.ConnectionState ?? ConnectionState;
                    LastDisconnectStatusCode = payloadResponse.LastDisconnectStatusCode ?? LastDisconnectStatusCode;
                    LastDisconnectReason = payloadResponse.LastDisconnectReason ?? LastDisconnectReason;
                    PairingInProgress = payloadResponse.PairingInProgress;
                    LastValidatedAt = DateTime.Now;
                }
                if (!response.IsSuccessStatusCode)
                {
                    return payloadResponse ?? new BaileysSendResponse
                    {
                        Success = false,
                        Message = ToFriendlyError(content)
                    };
                }

                return payloadResponse
                    ?? new BaileysSendResponse
                    {
                        Success = true,
                        Message = "Permintaan WhatsApp lokal berhasil."
                    };
            }
            catch (Exception ex)
            {
                return new BaileysSendResponse
                {
                    Success = false,
                    Message = ToFriendlyError(ex.Message)
                };
            }
        }

        private void ApplyHealthSnapshot(BaileysHealthResponse health)
        {
            IsReachable = true;
            IsConnected = health.Connected;
            IsPaired = health.Paired;
            UpdateDisconnectClock(health.Connected);
            LastPairingCode = health.PairingCode;
            LastError = health.Error;
            ConnectionState = health.ConnectionState;
            LastDisconnectStatusCode = health.LastDisconnectStatusCode;
            LastDisconnectReason = health.LastDisconnectReason;
            SidecarBuildTag = health.SidecarBuildTag;
            PairingInProgress = health.PairingInProgress;
            LastValidatedAt = DateTime.Now;
        }

        private void ApplyStatusSnapshot(BaileysSessionStatus status)
        {
            IsReachable = true;
            IsConnected = status.Connected;
            IsPaired = status.Paired;
            UpdateDisconnectClock(status.Connected);
            LastPairingCode = status.PairingCode;
            LastError = status.Error;
            ConnectionState = status.ConnectionState;
            LastDisconnectStatusCode = status.LastDisconnectStatusCode;
            LastDisconnectReason = status.LastDisconnectReason;
            SidecarBuildTag = status.SidecarBuildTag;
            PairingInProgress = status.PairingInProgress;
            LastValidatedAt = DateTime.Now;
        }

        private void UpdateDisconnectClock(bool connected)
        {
            if (connected)
            {
                _disconnectedSince = null;
                return;
            }

            _disconnectedSince ??= DateTime.Now;
        }

        public string BuildActionHint()
        {
            if (!IsReachable)
            {
                return "WhatsApp lokal terputus. Coba hubungkan ulang atau jalankan perbaikan setup.";
            }

            if (IsPaired && IsConnected)
            {
                return "WhatsApp lokal siap kirim.";
            }

            if (IsPaired && !IsConnected)
            {
                return "WhatsApp lokal sudah paired, sedang reconnect. Tunggu beberapa detik lalu coba lagi.";
            }

            if (PairingInProgress)
            {
                return "Masukkan pairing code di WhatsApp pada nomor bot.";
            }

            if (string.Equals(ConnectionState, "close", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ConnectionState, "closed", StringComparison.OrdinalIgnoreCase))
            {
                return "WhatsApp lokal terputus. Coba hubungkan ulang.";
            }

            if (!string.IsNullOrWhiteSpace(LastError))
            {
                return LastError;
            }

            return "Generate pairing code untuk menghubungkan WhatsApp lokal.";
        }

        private async Task TryRecoverOutboundAsync()
        {
            await RefreshHealthAsync();
            if (CanSendOutbound())
            {
                return;
            }

            if (!IsReachable || string.Equals(ConnectionState, "close", StringComparison.OrdinalIgnoreCase) || string.Equals(ConnectionState, "closed", StringComparison.OrdinalIgnoreCase))
            {
                await RestartSidecarAsync();
                await RefreshHealthAsync();
            }
        }

        private static bool ShouldRecoverOutbound(BaileysSendResponse response)
        {
            return string.Equals(response.Reason, "connection-closed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response.Reason, "not-ready", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response.Reason, "upstream-failure", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".csv" => "text/csv",
                ".zip" => "application/zip",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }

        private void HandleProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                LastError = ToFriendlyError(e.Data);
            }
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        private static string? ResolvePathOrCommand(string? path, bool allowCommand = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (allowCommand && !path.Contains(Path.DirectorySeparatorChar) && !path.Contains(Path.AltDirectorySeparatorChar))
            {
                return path;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string outputRelative = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            if (File.Exists(outputRelative) || Directory.Exists(outputRelative))
            {
                return outputRelative;
            }

            string? projectRoot = TryGetProjectRoot();
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                string projectRelative = Path.Combine(projectRoot, path);
                if (File.Exists(projectRelative) || Directory.Exists(projectRelative))
                {
                    return projectRelative;
                }
            }

            return outputRelative;
        }

        private static string? ResolveWorkingDirectory(BaileysSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory))
            {
                string outputRelative = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settings.WorkingDirectory);
                string? projectRoot = TryGetProjectRoot();
                string? projectRelative = string.IsNullOrWhiteSpace(projectRoot)
                    ? null
                    : Path.Combine(projectRoot, settings.WorkingDirectory);

                if (DirectoryHasRequiredSidecarModules(outputRelative))
                {
                    return outputRelative;
                }

                if (!string.IsNullOrWhiteSpace(projectRelative) && DirectoryHasRequiredSidecarModules(projectRelative))
                {
                    return projectRelative;
                }

                if (Directory.Exists(outputRelative))
                {
                    return outputRelative;
                }

                if (!string.IsNullOrWhiteSpace(projectRelative) && Directory.Exists(projectRelative))
                {
                    return projectRelative;
                }
            }

            string? entry = ResolvePathOrCommand(settings.SidecarEntryPath);
            return string.IsNullOrWhiteSpace(entry) ? null : Path.GetDirectoryName(entry);
        }

        private static string? ResolveSidecarEntryPath(BaileysSettings settings, string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(settings.SidecarEntryPath))
            {
                return null;
            }

            if (Path.IsPathRooted(settings.SidecarEntryPath))
            {
                return settings.SidecarEntryPath;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                string fromWorkingDir = Path.Combine(workingDirectory, Path.GetFileName(settings.SidecarEntryPath));
                if (File.Exists(fromWorkingDir))
                {
                    return fromWorkingDir;
                }
            }

            return ResolvePathOrCommand(settings.SidecarEntryPath);
        }

        private static string BuildAuthorizedNumbersEnv(BaileysSettings settings)
        {
            var values = new List<string>();
            AddAuthorizedNumber(values, settings.BotPhoneNumber);

            foreach (string number in settings.OwnerNumbers ?? new List<string>())
            {
                AddAuthorizedNumber(values, number);
            }

            foreach (string number in settings.KasirNumbers ?? new List<string>())
            {
                AddAuthorizedNumber(values, number);
            }

            return string.Join(",", values.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void AddAuthorizedNumber(List<string> values, string? number)
        {
            string normalized = AutomationEngine.NormalizeWhatsAppNumber(number);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                values.Add(normalized);
            }
        }

        private static string? TryGetProjectRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                string? candidate = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    break;
                }

                if (File.Exists(Path.Combine(candidate, "SmartSembakoAssistant.csproj")))
                {
                    return candidate;
                }

                current = candidate;
            }

            return null;
        }

        private static bool DirectoryHasRequiredSidecarModules(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            string nodeModules = Path.Combine(path, "node_modules");
            return File.Exists(Path.Combine(nodeModules, "pino", "package.json")) &&
                   File.Exists(Path.Combine(nodeModules, "@whiskeysockets", "baileys", "package.json"));
        }

        private static bool IsDevelopmentSidecarDirectory(string workingDirectory)
        {
            string? projectRoot = TryGetProjectRoot();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return false;
            }

            string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
            string fullProjectRoot = Path.GetFullPath(projectRoot);
            return fullWorkingDirectory.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveNodeBinaryPath(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (string.Equals(configuredPath, "node", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configuredPath, "node.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(RuntimePaths.BundledNodeBinaryPath))
                    {
                        return RuntimePaths.BundledNodeBinaryPath;
                    }

                    return configuredPath;
                }

                if (File.Exists(configuredPath))
                {
                    return configuredPath;
                }

                string resolved = ResolvePathOrCommand(configuredPath) ?? configuredPath;
                if (File.Exists(resolved))
                {
                    return resolved;
                }
            }

            string[] candidates =
            {
                RuntimePaths.BundledNodeBinaryPath,
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
                "node",
                "node.exe"
            };

            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, "node", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, "node.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? ResolveNpmBinaryPath()
        {
            string[] candidates =
            {
                "npm.cmd",
                "npm",
                @"C:\Program Files\nodejs\npm.cmd",
                @"C:\Program Files (x86)\nodejs\npm.cmd"
            };

            foreach (string candidate in candidates)
            {
                if (candidate.Equals("npm.cmd", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Equals("npm", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string ToFriendlyError(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "WhatsApp lokal belum siap.";
            }

            if (raw.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
            {
                return "WhatsApp lokal belum dijalankan. Klik hubungkan WhatsApp atau jalankan perbaikan setup.";
            }

            if (raw.Contains("Socket belum siap", StringComparison.OrdinalIgnoreCase))
            {
                return "WhatsApp lokal belum siap. Tunggu beberapa detik lalu coba lagi.";
            }

            if (raw.Contains("Nomor pairing belum valid", StringComparison.OrdinalIgnoreCase))
            {
                return "Nomor bot WhatsApp belum valid. Gunakan format nomor aktif tanpa spasi aneh.";
            }

            if (raw.Contains("Session logged out", StringComparison.OrdinalIgnoreCase))
            {
                return "Sesi WhatsApp perlu direset lalu dipairing ulang.";
            }

            if (raw.Contains("Connection Closed", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Precondition Required", StringComparison.OrdinalIgnoreCase))
            {
                return "Koneksi WhatsApp terputus saat meminta pairing.";
            }

            if (raw.Contains("405", StringComparison.OrdinalIgnoreCase))
            {
                return "Baileys sedang bermasalah di upstream saat pairing. Coba lagi beberapa menit lagi.";
            }

            if (raw.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("rate", StringComparison.OrdinalIgnoreCase))
            {
                return "Permintaan pairing terlalu sering. Tunggu beberapa menit lalu coba lagi.";
            }

            if (raw.Contains("Pairing code sedang dibuat", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("pairing-in-progress", StringComparison.OrdinalIgnoreCase))
            {
                return "Kode pairing sedang dibuat. Tunggu beberapa detik.";
            }

            if (raw.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("npm", StringComparison.OrdinalIgnoreCase))
            {
                return "Dependency WhatsApp lokal belum terpasang dengan benar. Jalankan perbaikan setup.";
            }

            return raw.Trim();
        }

        private static string FormatPairingCodeForLog(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "-";
            }

            string normalized = new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            return normalized.Length > 4
                ? $"{normalized[..4]}-{normalized[4..]}"
                : normalized;
        }

        private static string TrimMessage(string stderr, string stdout)
        {
            string candidate = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
            string[] lines = candidate
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .ToArray();
            return lines.Length == 0 ? "unknown error" : string.Join(" | ", lines);
        }

        private sealed class BaileysHealthResponse
        {
            public bool Connected { get; set; }
            public bool Paired { get; set; }
            public string? PairingCode { get; set; }
            public bool QrAvailable { get; set; }
            public string? QrDataUrl { get; set; }
            public DateTime? QrCodeCreatedAt { get; set; }
            public DateTime? QrCodeExpiresAt { get; set; }
            public bool QrInProgress { get; set; }
            public bool PairingInProgress { get; set; }
            public string? ConnectionState { get; set; }
            public int? LastDisconnectStatusCode { get; set; }
            public string? LastDisconnectReason { get; set; }
            public DateTime? LastSeen { get; set; }
            public string? SessionPath { get; set; }
            public string? SidecarBuildTag { get; set; }
            public int[]? BaileysVersion { get; set; }
            public string[]? Browser { get; set; }
            public string? Error { get; set; }
        }

        private sealed class BaileysSendResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string? ExternalMessageId { get; set; }
            public string? PairingCode { get; set; }
            public DateTime? PairingCodeExpiresAt { get; set; }
            public bool QrAvailable { get; set; }
            public string? QrDataUrl { get; set; }
            public DateTime? QrCodeExpiresAt { get; set; }
            public bool QrInProgress { get; set; }
            public int? RetryAfterSeconds { get; set; }
            public string? Reason { get; set; }
            public string? ConnectionState { get; set; }
            public int? LastDisconnectStatusCode { get; set; }
            public string? LastDisconnectReason { get; set; }
            public bool PairingInProgress { get; set; }
        }
    }

    public sealed class BaileysPairingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? PairingCode { get; set; }
        public string? Reason { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class BaileysQrPairingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool QrAvailable { get; set; }
        public string? QrDataUrl { get; set; }
        public string? Reason { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class BaileysSessionStatus
    {
        public bool Connected { get; set; }
        public bool Paired { get; set; }
        public string? PairingCode { get; set; }
        public DateTime? PairingCodeCreatedAt { get; set; }
        public DateTime? PairingCodeExpiresAt { get; set; }
        public bool QrAvailable { get; set; }
        public string? QrDataUrl { get; set; }
        public DateTime? QrCodeCreatedAt { get; set; }
        public DateTime? QrCodeExpiresAt { get; set; }
        public bool QrInProgress { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public bool PairingInProgress { get; set; }
        public string? ConnectionState { get; set; }
        public int? LastDisconnectStatusCode { get; set; }
        public string? LastDisconnectReason { get; set; }
        public DateTime? LastSeen { get; set; }
        public string? SessionPath { get; set; }
        public string? SidecarBuildTag { get; set; }
        public int[]? BaileysVersion { get; set; }
        public string[]? Browser { get; set; }
        public string? Error { get; set; }
    }
}
