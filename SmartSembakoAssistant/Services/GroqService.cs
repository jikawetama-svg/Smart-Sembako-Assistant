using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class GroqService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string? _fallbackApiKey;
        private readonly string? _fallbackModel;
        private bool _disposed = false;

        public GroqService(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService;
            _loggingService = loggingService;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var config = configService.Config;
            _apiKey = config?.Groq?.ApiKey ?? "";
            _model = config?.Groq?.Model ?? "llama-3.1-8b-instant";
            _fallbackApiKey = config?.Groq?.FallbackApiKey;
            _fallbackModel = config?.Groq?.FallbackModel ?? "gemini-3.1-flash-lite-preview";
        }

        /// <summary>
        /// Dispose HttpClient untuk mencegah resource leak
        /// </summary>
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
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        public async Task<string> SendPromptAsync(string systemPrompt, string userPrompt, double temperature = 0.7, int maxTokens = 1000)
        {
            try
            {
                return await SendGroqRequestAsync(systemPrompt, userPrompt, temperature, maxTokens);
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                
                // Detect specific API errors
                if (errorMessage.Contains("401") || errorMessage.Contains("Unauthorized"))
                {
                    await _loggingService.LogErrorAsync(
                        "Groq API Key tidak valid atau expired (401 Unauthorized)", 
                        "AI", 
                        ex.ToString());
                    return "❌ **Error API Key**\n\nAPI Key Groq tidak valid atau sudah expired. Silakan update di Settings.";
                }
                
                if (errorMessage.Contains("429") || errorMessage.Contains("rate limit"))
                {
                    await _loggingService.LogWarningAsync(
                        "Groq Rate Limit exceeded", 
                        "AI", 
                        ex.ToString());
                    return "⚠️ **Limit AI Tercapai**\n\nKuota AI harian habis. Coba lagi besok atau upgrade Groq ke Dev Tier.";
                }
                
                // Fallback ke Gemini jika Groq gagal (bukan karena auth error)
                if (!string.IsNullOrEmpty(_fallbackApiKey) && !errorMessage.Contains("401"))
                {
                    try
                    {
                        return await SendGeminiRequestAsync(systemPrompt, userPrompt, temperature, maxTokens);
                    }
                    catch (Exception fallbackEx)
                    {
                        string fallbackError = fallbackEx.Message;
                        
                        if (fallbackError.Contains("404") || fallbackError.Contains("Not Found"))
                        {
                            await _loggingService.LogErrorAsync(
                                "Gemini model tidak ditemukan (404)", 
                                "AI", 
                                fallbackEx.ToString());
                        }
                        else
                        {
                            await _loggingService.LogErrorAsync(
                                $"Fallback Gemini error: {fallbackError}", 
                                "AI", 
                                fallbackEx.ToString());
                        }
                    }
                }

                return $"⚠️ AI sedang gangguan. Silakan coba lagi nanti.";
            }
        }

        private async Task<string> SendGroqRequestAsync(string systemPrompt, string userPrompt, double temperature, int maxTokens)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = temperature,
                max_tokens = maxTokens
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request);
            
            // Handle rate limit error specially
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                response.StatusCode == (System.Net.HttpStatusCode)413)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                await _loggingService.LogErrorAsync(
                    $"Groq Rate Limit exceeded. Response: {errorContent}", 
                    "AI", 
                    "Rate Limit - Please reduce prompt size or upgrade plan");
                
                return "⚠️ **Limit AI Tercapai**\n\n" +
                       "Maaf, kuota AI harian telah habis. Solusi:\n" +
                       "1. Tunggu hingga besok (reset harian)\n" +
                       "2. Upgrade Groq ke Dev Tier untuk lebih banyak token\n" +
                       "3. Gunakan fitur tanpa AI sementara\n\n" +
                       "(Error: Rate Limit Exceeded)";
            }
            
            // Handle permission error
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                await _loggingService.LogErrorAsync(
                    $"Groq Permission denied. Response: {errorContent}", 
                    "AI", 
                    "Model permission blocked");
                
                return "❌ **Error AI Model**\n\n" +
                       "Model AI tidak tersedia atau diblokir. " +
                       "Silakan cek konfigurasi model di Settings.";
            }

            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseJson);

            return jsonResponse?["choices"]?[0]?["message"]?["content"]?.ToString()
                   ?? "Maaf, saya tidak dapat memproses permintaan Anda.";
        }

        private async Task<string> SendGeminiRequestAsync(string systemPrompt, string userPrompt, double temperature, int maxTokens)
        {
            // Gemini API format
            string combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = combinedPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = temperature,
                    maxOutputTokens = maxTokens
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_fallbackModel}:generateContent?key={_fallbackApiKey}";
            
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseJson);

            return jsonResponse?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()
                   ?? "Maaf, saya tidak dapat memproses permintaan Anda.";
        }

        public async Task<string> ParseReceiptAsync(string ocrText)
        {
            string systemPrompt = @"AI parser untuk struk belanja. Ekstrak: toko, tanggal, item (nama, qty, harga), total.
Output JSON: {""store_name"":"""",""date"":"""",""items"":[{""product_name"":"""",""quantity"":0,""total"":0}],""total"":0}";

            return await SendPromptAsync(systemPrompt, $"Struk:\n{ocrText}", 0.3, 500);
        }

        /// <summary>
        /// Test Groq API connection dengan prompt minimal
        /// </summary>
        public async Task<(bool Success, string Message)> TestGroqConnectionAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_GROQ_API_KEY")
                {
                    return (false, "API Key Groq belum diisi");
                }

                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Hi" }
                    },
                    max_tokens = 10
                };

                string jsonBody = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string errorMsg = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return (false, "API Key tidak valid (401 Unauthorized)");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return (false, $"Model '{_model}' tidak tersedia atau tidak diizinkan");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        return (false, "Rate limit tercapai (429)");
                    }
                    
                    return (false, errorMsg);
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseJson);
                
                string reply = jsonResponse?["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                
                if (string.IsNullOrEmpty(reply))
                {
                    return (false, "Response kosong dari API");
                }

                return (true, $"✅ Connected! Model: {_model}");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                return (false, "Timeout - koneksi terlalu lambat");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Test Gemini API connection dengan prompt minimal
        /// </summary>
        public async Task<(bool Success, string Message)> TestGeminiConnectionAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_fallbackApiKey) || _fallbackApiKey == "YOUR_GEMINI_API_KEY")
                {
                    return (false, "API Key Gemini belum diisi");
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = "Hi" }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens = 10
                    }
                };

                string jsonBody = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_fallbackModel}:generateContent?key={_fallbackApiKey}";

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return (false, $"Model '{_fallbackModel}' tidak ditemukan (404)");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                             response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return (false, "API Key tidak valid (401/403)");
                    }
                    
                    return (false, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseJson);
                
                string reply = jsonResponse?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() ?? "";
                
                if (string.IsNullOrEmpty(reply))
                {
                    return (false, "Response kosong dari API");
                }

                return (true, $"✅ Connected! Model: {_fallbackModel}");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                return (false, "Timeout - koneksi terlalu lambat");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task<string> GenerateRestockRecommendationAsync(
            List<Product> lowStockProducts,
            List<Product> expiringProducts,
            decimal todayRevenue,
            decimal todayProfit,
            List<string> recentConversations,
            List<Product>? topSellingProducts = null)
        {
            // Kurangi produk info ke 5 saja (dari 10)
            string contextInfo = string.Join("\n", recentConversations.Take(3));

            string productsInfo = "📦 STOK RENDAH:\n";
            foreach (var product in lowStockProducts.Take(8))
            {
                string status = product.Stock <= 0 ? "🔴 HABIS" : product.Stock <= 5 ? "🟡 RENDAH" : "🟠 PERLU RESTOCK";
                productsInfo += $"- {product.Name}: Stok {product.Stock} {product.Unit} ({status}), Margin {product.Margin:F1}%\n";
            }

            if (expiringProducts.Any())
            {
                productsInfo += "\n⚠️ HAMPIR EXPIRY:\n";
                foreach (var product in expiringProducts.Take(5))
                {
                    productsInfo += $"- {product.Name}: Stok {product.Stock}, Exp {product.ExpiryDate:dd/MM/yy}\n";
                }
            }

            if (topSellingProducts != null && topSellingProducts.Any())
            {
                productsInfo += "\n🔥 PRODUK TERLARIS HARI INI:\n";
                foreach (var product in topSellingProducts.Take(5))
                {
                    productsInfo += $"- {product.Name}: Terjual {product.Stock} {product.Unit}\n";
                }
            }

            string userPrompt = $@"Data toko hari ini:
Revenue: Rp {todayRevenue:N0}
Profit: Rp {todayProfit:N0}

{productsInfo}

Buatkan rekomendasi restock yang:
1. Prioritaskan produk terlaris yang stoknya rendah
2. Pertimbangkan margin tinggi
3. Hindari produk hampir expiry
4. Sarankan jumlah restock yang spesifik
5. Minta konfirmasi jika >50 pcs atau >Rp500rb

Format: Gunakan emoji, jelas, langsung ke inti. JANGAN bertele-tele. MAKSIMAL 150 kata.";

            string systemPrompt = @"Asisten AI toko sembako. Tugas: Beri rekomendasi restock cerdas.

ATURAN:
1. JANGAN minta maaf
2. JANGAN bertele-tele, langsung ke inti
3. Gunakan data yang diberikan, JANGAN mengarang
4. Format rapi dengan emoji
5. Jawaban MAKSIMAL 150 kata";

            return await SendPromptAsync(systemPrompt, userPrompt, 0.7, 600);
        }

        public async Task<string> GenerateNaturalResponseAsync(
            string userMessage,
            List<string> conversationHistory,
            string? userRole = null,
            string? realStoreData = null)
        {
            // Kurangi history dari 8 jadi 4 untuk hemat tokens
            string contextInfo = conversationHistory.Any()
                ? "RIWAYAT PERCAKAPAN:\n" + string.Join("\n", conversationHistory.Take(4))
                : "";

            // Tambah data real toko jika ada
            string storeDataInfo = realStoreData ?? "\n\n(Tidak ada data toko tersedia - JANGAN mengarang data!)";

            string userPrompt = $@"{contextInfo}

{storeDataInfo}

PERTANYAAN USER: {userMessage}

PENTING: Gunakan HANYA data dari 'DATA REAL TOKO' di atas. JANGAN mengarang angka atau fakta!";

            // System prompt yang lebih komprehensif
            string systemPrompt = $@"Asisten AI pintar untuk pemilik toko sembako (SSA).

KONTEKS DATABASE & STRUKTUR TOKO:
Anda terhubung ke database Aronium POS. Pahami struktur ini:
1. TABEL PRODUK: Memiliki Nama, Stok (bisa minus/negatif), Harga Modal (Cost), Harga Jual (Price), Margin.
2. TABEL PELANGGAN (Customer): Memiliki Nama, No HP, Riwayat Belanja.
3. TABEL TRANSAKSI (Document): Memiliki Tanggal, Total, Item Produk.
4. STOK MINUS: Di Aronium, stok BISA negatif (minus). Ini artinya barang terjual tapi stok catatan lebih kecil dari 0 (biasanya karena barang fisik ada tapi belum diinput, atau hutang stok). JANGAN bilang stok 0 jika datanya minus. Bilang 'Minus X'.

PERAN ANDA SAAT INI: {userRole}
{(userRole == "Owner" ? "- Anda berbicara dengan PEMILIK TOKO. Berikan insight mendalam, profit, strategi, dan laporan lengkap." : "- Anda berbicara dengan KASIR. Fokus pada operasional, stok, dan harga jual. JANGAN sebutkan data Profit atau Harga Modal jika ditanya, bilang 'Data ini hanya untuk Owner'.")}

ATURAN PENTING (WAJIB DIPATUHI):
1. Gunakan HANYA data real yang diberikan di prompt. JANGAN mengarang angka, fakta, atau tabel.
2. Jika data tidak ada di prompt, bilang jujur: ""Data tidak tersedia di sistem.""
3. Bahasa Indonesia natural, profesional, tapi luwes (seperti asisten toko).
4. JANGAN minta maaf berlebihan. Cukup sekali jika memang perlu.
5. Jawab SINGKAT & PADAT. Langsung ke inti. MAKSIMAL 3-4 kalimat untuk jawaban sederhana.
6. JANGAN gunakan basa-basi seperti 'Halo!', 'Tentu!', 'Baiklah!' di awal kalimat.
7. JANGAN bilang 'Maaf saya tidak bisa' jika Anda bisa menyajikan data dalam bentuk tabel teks.
8. PROAKTIF: Jika Anda melihat data Stok Minus atau Produk Tidak Laku (Slow Moving) di data yang diberikan, sampaikan sebagai peringatan di awal jawaban Anda.
9. ANTI-HALUSINASI: JANGAN PERNAH mengarang data tabel, angka transaksi, atau riwayat belanja yang tidak ada di prompt. Jika user tanya detail yang tidak ada, bilang ""Data detail tidak tersedia.""
10. JANGAN beri saran/rekomendasi yang tidak diminta user. Jawab sesuai pertanyaan saja.

KEMAMPUAN ANALISIS & LAPORAN:
- **Stok:** Bisa analisa stok aman, rendah, habis, dan MINUS.
- **Pelanggan:** Bisa sebutkan nama pelanggan loyal (yang sering belanja/belanja banyak).
- **Laba/Rugi:** Hitung (Harga Jual - Harga Modal) * Terjual. Jika Modal 0, katakan 'Belum diinput'.
- **Tren:** Bandingkan hari ini vs kemarin.
- **Tampilan Data:** Jika user minta 'daftar', 'data', atau 'laporan', tampilkan dalam format TABEL MARKDOWN yang rapi agar mudah dibaca atau di-copy ke Excel.
- **Riwayat Pelanggan:** Jika ada data riwayat belanja pelanggan di prompt, gunakan itu. JANGAN mengarang transaksi.

CONTOH FORMAT TABEL:
| Nama Produk | Stok | Harga Modal | Harga Jual |
|-------------|------|-------------|------------|
| Kopi        | -5   | Rp 5.000    | Rp 7.000   |

CONTOH JAWABAN YANG BENAR:
✅ User: ""Stok kapal api berapa?""
✅ AI: ""Stok Kapal Api Mix: -6 Rcg (Minus).""

✅ User: ""Wawan belanja apa aja?""
✅ AI: ""Data riwayat belanja Wawan tidak tersedia di sistem."" (JIKA DATA TIDAK ADA DI PROMPT)

✅ User: ""Tampilkan data stok minus""
✅ AI: ""Berikut data produk dengan stok minus:
| Produk | Stok |
|--------|------|
| Roti   | -10  |""

❌ JAWABAN SALAH (HALUSINASI):
❌ ""Wawan telah melakukan 22 kali belanja dengan total Rp 8.203.150. Berikut rinciannya: [tabel karangan]""

{(userRole == "Owner" ? "- Fokus insight bisnis, profit, & strategi" : "- Fokus stok & transaksi")}";

            return await SendPromptAsync(systemPrompt, userPrompt, 0.7, 800);
        }
    }
}
