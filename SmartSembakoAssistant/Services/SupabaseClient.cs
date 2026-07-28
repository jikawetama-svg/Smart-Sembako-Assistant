using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class SupabaseClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SupabaseSettings? _settings;
        private bool _disposed;

        public SupabaseClient(ConfigService configService)
        {
            _settings = configService.Config?.Supabase;
            _httpClient = new HttpClient();

            ConfigureHttpClient();
        }

        public SupabaseClient(SupabaseSettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();

            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            if (_settings == null || string.IsNullOrWhiteSpace(_settings.Url))
                return;

            string baseUrl = _settings.Url.TrimEnd('/');
            if (!baseUrl.EndsWith("/rest/v1"))
            {
                baseUrl += "/rest/v1";
            }

            _httpClient.BaseAddress = new Uri(baseUrl + "/");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);

            string token = _settings.ApiKey ?? "";
            if (!string.IsNullOrWhiteSpace(_settings.JwtToken) && _settings.JwtToken.Contains("."))
            {
                token = _settings.JwtToken;
            }

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", _settings.ApiKey);
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private bool HasValidTenantConfiguration(out string error)
        {
            error = string.Empty;
            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.Url))
            {
                error = "Supabase tidak aktif atau belum terkonfigurasi.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_settings.MerchantId))
            {
                error = "MerchantId belum tersedia. Buka ulang aplikasi agar config membuat MerchantId otomatis, atau isi manual di Settings.";
                return false;
            }

            return true;
        }

        public async Task<(bool success, string message)> TestConnectionAsync()
        {
            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, configError);
            }

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success)
                {
                    return (false, bootstrap.error ?? "Bootstrap merchant gagal.");
                }

                var response = await _httpClient.GetAsync("products_sync?select=id&limit=1");
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Koneksi Supabase berhasil.");
                }

                string errText = await response.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)response.StatusCode}: {errText}");
            }
            catch (Exception ex)
            {
                return (false, $"Gagal terhubung ke Supabase: {ex.Message}");
            }
        }

        public async Task<(bool success, int count, string? error)> UpsertProductsAsync(List<ProductSyncDTO> products)
        {
            if (products == null || products.Count == 0)
            {
                return (true, 0, null);
            }

            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, 0, configError);
            }

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success)
                {
                    return (false, 0, bootstrap.error);
                }

                string json = JsonConvert.SerializeObject(products);
                using var request = CreateUpsertPostRequest("products_sync", json);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, products.Count, null);
                }

                string errBody = await response.Content.ReadAsStringAsync();
                return (false, 0, $"HTTP {(int)response.StatusCode}: {errBody}");
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        private HttpRequestMessage CreateUpsertPostRequest(string endpoint, string json, string? onConflict = null)
        {
            string url = string.IsNullOrWhiteSpace(onConflict)
                ? endpoint
                : $"{endpoint}?on_conflict={Uri.EscapeDataString(onConflict)}";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
            return request;
        }

        public async Task<(bool success, string? error)> UpsertTransactionSummaryAsync(TransactionSummaryDTO summary)
        {
            if (summary == null)
            {
                return (true, null);
            }

            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, configError);
            }

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success)
                {
                    return (false, bootstrap.error);
                }

                string json = JsonConvert.SerializeObject(summary);
                using var request = CreateUpsertPostRequest("transactions_summary", json);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, null);
                }

                string errBody = await response.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)response.StatusCode}: {errBody}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string? error)> UpsertRestockSyncAsync(List<RestockSyncDTO> items)
        {
            if (items == null || items.Count == 0) return (true, null);
            if (!HasValidTenantConfiguration(out var configError)) return (false, configError);

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success) return (false, bootstrap.error);

                string json = JsonConvert.SerializeObject(items);
                using var request = CreateUpsertPostRequest("restock_sync", json);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, null)
                    : (false, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public async Task<(bool success, string? error)> UpsertInventorySyncAsync(List<InventorySyncDTO> items)
        {
            if (items == null || items.Count == 0) return (true, null);
            if (!HasValidTenantConfiguration(out var configError)) return (false, configError);

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success) return (false, bootstrap.error);

                string json = JsonConvert.SerializeObject(items);
                using var request = CreateUpsertPostRequest("inventory_sync", json);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, null)
                    : (false, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public async Task<(bool success, int count, string? error)> UpsertCustomersAsync(List<CustomerSyncDTO> customers)
        {
            if (customers == null || customers.Count == 0) return (true, 0, null);
            if (!HasValidTenantConfiguration(out var configError)) return (false, 0, configError);

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success) return (false, 0, bootstrap.error);

                string json = JsonConvert.SerializeObject(customers);
                using var request = CreateUpsertPostRequest("customers_sync", json);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, customers.Count, null)
                    : (false, 0, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { return (false, 0, ex.Message); }
        }

        public async Task<(bool success, int count, string? error)> UpsertSuppliersAsync(List<SupplierSyncDTO> suppliers)
        {
            if (suppliers == null || suppliers.Count == 0) return (true, 0, null);
            if (!HasValidTenantConfiguration(out var configError)) return (false, 0, configError);

            try
            {
                var bootstrap = await EnsureMerchantBootstrapAsync();
                if (!bootstrap.success) return (false, 0, bootstrap.error);

                string json = JsonConvert.SerializeObject(suppliers);
                using var request = CreateUpsertPostRequest("suppliers_sync", json);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, suppliers.Count, null)
                    : (false, 0, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { return (false, 0, ex.Message); }
        }

        public async Task<(bool success, List<AgentCommandQueueItem> commands, string? error)> GetPendingAgentCommandsAsync(int limit = 10)
        {
            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, new List<AgentCommandQueueItem>(), configError);
            }

            try
            {
                string merchantFilter = Uri.EscapeDataString($"eq.{_settings!.MerchantId}");
                string url =
                    "agent_command_queue" +
                    "?select=id,merchant_id,source_channel,source_chat_id,source_user_id,command_text,command_kind,status,created_at" +
                    $"&merchant_id={merchantFilter}" +
                    "&status=eq.pending" +
                    "&order=created_at.asc" +
                    $"&limit={Math.Max(1, Math.Min(limit, 25))}";
                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return (false, new List<AgentCommandQueueItem>(), $"HTTP {(int)response.StatusCode}: {body}");
                }

                var commands = JsonConvert.DeserializeObject<List<AgentCommandQueueItem>>(body) ?? new List<AgentCommandQueueItem>();
                return (true, commands, null);
            }
            catch (Exception ex)
            {
                return (false, new List<AgentCommandQueueItem>(), ex.Message);
            }
        }

        public async Task<(bool success, string? error)> ClaimAgentCommandAsync(string commandId, string claimedBy)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                return (false, "Command id kosong.");
            }
            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, configError);
            }

            var payload = new
            {
                status = "processing",
                claimed_by = claimedBy,
                claimed_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            return await PatchAgentCommandAsync(commandId, payload, requirePending: true);
        }

        public async Task<(bool success, string? error)> CompleteAgentCommandAsync(string commandId, string resultText)
        {
            var payload = new
            {
                status = "completed",
                result_text = resultText,
                completed_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            return await PatchAgentCommandAsync(commandId, payload);
        }

        public async Task<(bool success, string? error)> FailAgentCommandAsync(string commandId, string errorMessage)
        {
            var payload = new
            {
                status = "failed",
                error_message = errorMessage,
                completed_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            return await PatchAgentCommandAsync(commandId, payload);
        }

        private async Task<(bool success, string? error)> PatchAgentCommandAsync(string commandId, object payload, bool requirePending = false)
        {
            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, configError);
            }

            try
            {
                string url = $"agent_command_queue?id=eq.{Uri.EscapeDataString(commandId)}";
                if (requirePending)
                {
                    url += "&status=eq.pending";
                }

                string json = JsonConvert.SerializeObject(payload);
                using var request = new HttpRequestMessage(HttpMethod.Patch, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Prefer", "return=minimal");
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, null);
                }

                return (false, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool success, string? error)> EnsureMerchantBootstrapAsync()
        {
            if (_settings == null || string.IsNullOrWhiteSpace(_settings.MerchantId))
            {
                return (false, "MerchantId belum tersedia.");
            }

            try
            {
                var merchantPayload = new[]
                {
                    new
                    {
                        id = _settings.MerchantId,
                        display_name = _settings.MerchantId,
                        timezone = "Asia/Jakarta",
                        status = "active"
                    }
                };
                using var merchantRequest = CreateUpsertPostRequest("merchants", JsonConvert.SerializeObject(merchantPayload));
                var merchantResponse = await _httpClient.SendAsync(merchantRequest);

                // Older schemas may not have merchants table yet. In that case, keep
                // syncing against simple tables and let the schema update add it later.
                if (!merchantResponse.IsSuccessStatusCode &&
                    merchantResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    string body = await merchantResponse.Content.ReadAsStringAsync();
                    return (false, $"Bootstrap merchant gagal HTTP {(int)merchantResponse.StatusCode}: {body}");
                }

                if (!string.IsNullOrWhiteSpace(_settings.DeviceId))
                {
                    var devicePayload = new[]
                    {
                        new
                        {
                            merchant_id = _settings.MerchantId,
                            device_id = _settings.DeviceId,
                            label = Environment.MachineName,
                            last_seen_at = DateTime.UtcNow
                        }
                    };
                    using var deviceRequest = CreateUpsertPostRequest(
                        "merchant_devices",
                        JsonConvert.SerializeObject(devicePayload),
                        "merchant_id,device_id");
                    var deviceResponse = await _httpClient.SendAsync(deviceRequest);
                    if (!deviceResponse.IsSuccessStatusCode &&
                        deviceResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        string body = await deviceResponse.Content.ReadAsStringAsync();
                        return (false, $"Bootstrap device gagal HTTP {(int)deviceResponse.StatusCode}: {body}");
                    }
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string? error)> UpdateSyncMetadataAsync(string key, string value)
        {
            if (!HasValidTenantConfiguration(out var configError))
            {
                return (false, configError);
            }

            try
            {
                string tenantKey = $"{_settings!.MerchantId}:{key}";
                var payload = new[]
                {
                    new
                    {
                        key = tenantKey,
                        merchant_id = _settings.MerchantId,
                        value = value,
                        updated_at = DateTime.UtcNow
                    }
                };
                string json = JsonConvert.SerializeObject(payload);
                using var request = CreateUpsertPostRequest("sync_metadata", json);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, null)
                    : (false, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
