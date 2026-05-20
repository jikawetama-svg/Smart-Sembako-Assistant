using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using SmartSembakoAssistant.Helpers;

namespace SmartSembakoAssistant.Services
{
    public class TunnelManager
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private Process? _process;
        private readonly Regex _urlRegex = new(@"https://[^\s""'>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private int? _startedPort;

        public bool IsRunning { get; private set; }
        public string? CurrentPublicUrl { get; private set; }
        public string Provider => _configService.Config?.Tunnel?.Provider ?? "cloudflared";

        public TunnelManager(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
        }

        public async Task<bool> StartAsync(int port)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                var tunnel = _configService.Config?.Tunnel;
                if (tunnel == null)
                {
                    await StopCoreAsync(log: false);
                    return true;
                }

                CurrentPublicUrl = FirstNonEmpty(tunnel.PublicUrl, _configService.Config?.WhatsApp?.PublicWebhookUrl);

                if (!tunnel.Enabled)
                {
                    await StopCoreAsync(log: false);
                    IsRunning = !string.IsNullOrWhiteSpace(CurrentPublicUrl);
                    return true;
                }

                if (string.Equals(Provider, "manual", StringComparison.OrdinalIgnoreCase))
                {
                    await StopCoreAsync(log: false);
                    IsRunning = !string.IsNullOrWhiteSpace(CurrentPublicUrl);
                    return IsRunning;
                }

                if (_process != null && !_process.HasExited && _startedPort == port)
                {
                    IsRunning = true;
                    return true;
                }

                string? binaryPath = ResolveTunnelBinary(tunnel.BinaryPath);
                if (string.IsNullOrWhiteSpace(binaryPath))
                {
                    string warning = !string.IsNullOrWhiteSpace(CurrentPublicUrl)
                        ? "Tunnel diaktifkan tetapi binary path tidak valid. Memakai Public URL manual yang sudah diisi."
                        : "Tunnel diaktifkan tetapi binary path tidak valid.";
                    await _loggingService.LogWarningAsync(warning, "Tunnel");
                    await StopCoreAsync(log: false);
                    IsRunning = !string.IsNullOrWhiteSpace(CurrentPublicUrl);
                    return IsRunning;
                }

                await StopCoreAsync(log: false);

                string args = BuildArgs(port);

                var startInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += HandleTunnelOutput;
                _process.ErrorDataReceived += HandleTunnelOutput;
                _process.Exited += async (_, _) =>
                {
                    IsRunning = false;
                    await _loggingService.LogWarningAsync("Tunnel process berhenti.", "Tunnel");
                };

                if (!_process.Start())
                {
                    return false;
                }

                _startedPort = port;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                IsRunning = true;
                await _loggingService.LogInfoAsync($"Tunnel process started with provider={Provider}, args={args}", "Tunnel");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Gagal memulai tunnel: {ex.Message}", "Tunnel", ex.ToString());
                IsRunning = false;
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
                await _loggingService.LogErrorAsync($"Gagal menghentikan tunnel: {ex.Message}", "Tunnel", ex.ToString());
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public string BuildArgs(int port)
        {
            var tunnel = _configService.Config?.Tunnel;
            string template = !string.IsNullOrWhiteSpace(tunnel?.ArgsTemplate)
                ? tunnel!.ArgsTemplate!
                : string.Equals(Provider, "cloudflared", StringComparison.OrdinalIgnoreCase)
                    ? "tunnel --url http://localhost:{port}"
                    : "http --url http://localhost:{port}";

            string args = template
                .Replace("{port}", port.ToString())
                .Replace("{path}", "/whatsapp/webhook");

            return AddCloudflaredHostHeader(args, port);
        }

        public async Task<(bool Success, string Message)> TestReachabilityAsync(int port)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync($"http://localhost:{port}/health/integrations");
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Listener local merespons {(int)response.StatusCode}.");
                }

                return (true, "Listener local merespons endpoint health.");
            }
            catch (Exception ex)
            {
                return (false, $"Health endpoint lokal gagal diakses: {ex.Message}");
            }
        }

        private void HandleTunnelOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            Match match = _urlRegex.Match(e.Data);
            if (!match.Success)
            {
                return;
            }

            CurrentPublicUrl = match.Value.TrimEnd('/');
            if (_configService.Config?.Tunnel != null)
            {
                _configService.Config.Tunnel.PublicUrl = CurrentPublicUrl;
            }

            if (_configService.Config?.WhatsApp != null)
            {
                _configService.Config.WhatsApp.PublicWebhookUrl = CurrentPublicUrl;
            }

            _configService.SaveConfig();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private string AddCloudflaredHostHeader(string args, int port)
        {
            if (!string.Equals(Provider, "cloudflared", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("--http-host-header", StringComparison.OrdinalIgnoreCase))
            {
                return args;
            }

            return $"{args} --http-host-header localhost:{port}";
        }

        private async Task StopCoreAsync(bool log)
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync();
            }

            _process?.Dispose();
            _process = null;
            _startedPort = null;
            IsRunning = false;

            if (log)
            {
                await _loggingService.LogInfoAsync("Tunnel dihentikan.", "Tunnel");
            }
        }

        private static string? ResolveTunnelBinary(string? configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return FindBundledOrLocalCloudflared();
            }

            string trimmed = configuredPath.Trim();
            if (File.Exists(trimmed))
            {
                return trimmed;
            }

            if (!Path.IsPathRooted(trimmed) &&
                (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar)))
            {
                string appRelative = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, trimmed);
                if (File.Exists(appRelative))
                {
                    return appRelative;
                }
            }

            if (!trimmed.Contains(Path.DirectorySeparatorChar) &&
                !trimmed.Contains(Path.AltDirectorySeparatorChar))
            {
                string? bundled = FindBundledOrLocalCloudflared();
                if (!string.IsNullOrWhiteSpace(bundled))
                {
                    return bundled;
                }

                return trimmed;
            }

            return FindBundledOrLocalCloudflared();
        }

        private static string? FindBundledOrLocalCloudflared()
        {
            var candidates = new List<string>
            {
                RuntimePaths.BundledCloudflaredBinaryPath,
                Path.Combine(RuntimePaths.AppBaseDirectory, "cloudflared.exe"),
                Path.Combine(Environment.CurrentDirectory, "cloudflared.exe")
            };

            string current = RuntimePaths.AppBaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                candidates.Add(Path.Combine(parent.FullName, "cloudflared.exe"));
                current = parent.FullName;
            }

            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
