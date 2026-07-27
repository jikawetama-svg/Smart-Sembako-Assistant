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

        public async Task<(bool success, string message)> TestConnectionAsync()
        {
            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.Url))
            {
                return (false, "Supabase tidak terkonfigurasi atau dinonaktifkan.");
            }

            try
            {
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

            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.Url))
            {
                return (false, 0, "Supabase tidak aktif atau belum terkonfigurasi.");
            }

            try
            {
                string json = JsonConvert.SerializeObject(products);
                using var request = new HttpRequestMessage(HttpMethod.Post, "products_sync")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                // Upsert header for Supabase REST API
                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

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

        public async Task<(bool success, string? error)> UpsertTransactionSummaryAsync(TransactionSummaryDTO summary)
        {
            if (summary == null)
            {
                return (true, null);
            }

            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.Url))
            {
                return (false, "Supabase tidak aktif.");
            }

            try
            {
                string json = JsonConvert.SerializeObject(summary);
                using var request = new HttpRequestMessage(HttpMethod.Post, "transactions_summary")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

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

        public async Task<(bool success, string? error)> UpdateSyncMetadataAsync(string key, string value)
        {
            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.Url))
            {
                return (false, "Supabase tidak aktif.");
            }

            try
            {
                var payload = new[] { new { key = key, value = value, updated_at = DateTime.UtcNow } };
                string json = JsonConvert.SerializeObject(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, "sync_metadata")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

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
