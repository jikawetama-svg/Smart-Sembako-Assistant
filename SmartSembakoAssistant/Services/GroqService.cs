using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private readonly string _visionModel;
        private const int MinimumOcrTokens = 1500;
        private static readonly string[] DefaultVisionModelCandidates =
        {
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-3.1-flash-lite",
            "gemini-3.1-flash"
        };
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
            _fallbackModel = config?.Groq?.FallbackModel ?? "gemini-3.1-flash-lite";
            _visionModel = config?.Groq?.VisionModel ?? "gemini-3.1-flash-lite";
        }

        public bool HasGeminiFallbackConfigured =>
            !string.IsNullOrWhiteSpace(_fallbackApiKey) &&
            !string.Equals(_fallbackApiKey, "YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);

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

        public async Task<string> ParseReceiptAsync(string ocrText, string? vendorType = null)
        {
            int configuredMaxTokens = _configService.Config?.Groq?.MaxTokens ?? 500;
            int ocrMaxTokens = Math.Max(configuredMaxTokens, MinimumOcrTokens);

            string systemPrompt = BuildReceiptParserPrompt(vendorType);
            string userPrompt = $"Vendor hint: {vendorType ?? "GENERIC"}\nStruk mentah OCR:\n{ocrText}";

            try
            {
                string groqResponse = await SendGroqRequestAsync(systemPrompt, userPrompt, 0.1, ocrMaxTokens);
                if (LooksLikeStructuredJson(groqResponse))
                {
                    return groqResponse;
                }

                throw new InvalidOperationException("Groq OCR response was not valid structured JSON.");
            }
            catch (Exception groqEx)
            {
                if (HasGeminiFallbackConfigured)
                {
                    try
                    {
                        string fallbackResponse = await SendGeminiRequestAsync(systemPrompt, userPrompt, 0.1, ocrMaxTokens);
                        if (LooksLikeStructuredJson(fallbackResponse))
                        {
                            return fallbackResponse;
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        await _loggingService.LogWarningAsync(
                            $"OCR Gemini fallback gagal: {fallbackEx.Message}",
                            "OCR",
                            fallbackEx.ToString());
                    }
                }

                throw new InvalidOperationException("OCR AI parsing gagal.", groqEx);
            }
        }

        public async Task<string> ParseReceiptVisionAsync(string imagePath, string? vendorType = null)
        {
            if (!HasGeminiFallbackConfigured)
            {
                throw new InvalidOperationException("Gemini API key belum dikonfigurasi untuk OCR Vision.");
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                throw new FileNotFoundException("File gambar untuk OCR Vision tidak ditemukan.", imagePath);
            }

            int configuredMaxTokens = _configService.Config?.Groq?.MaxTokens ?? 500;
            int ocrMaxTokens = Math.Max(configuredMaxTokens, MinimumOcrTokens);
            string prompt = BuildReceiptParserPrompt(vendorType) +
                            "\n\nLihat gambar struk/faktur ini langsung. Ekstrak item berdasarkan layout visual, bukan hanya tebakan dari OCR teks.";

            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);
            string mimeType = GetImageMimeType(imagePath);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = base64Image
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = ocrMaxTokens
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            List<string> errors = new();

            foreach (string model in GetVisionModelCandidates())
            {
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_fallbackApiKey}";

                var response = await _httpClient.PostAsync(url, content);
                string responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    string errorSummary = $"model={model}, status={(int)response.StatusCode} {response.ReasonPhrase}";
                    errors.Add(errorSummary);
                    await _loggingService.LogWarningAsync(
                        $"[OCR] Gemini Vision attempt gagal: {errorSummary}",
                        "OCR",
                        responseJson);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new InvalidOperationException($"Gemini Vision rate limited pada {model}.");
                    }

                    continue;
                }

                var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseJson);
                string? parsedText = jsonResponse?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(parsedText))
                {
                    if (!string.Equals(model, _visionModel, StringComparison.OrdinalIgnoreCase))
                    {
                        await _loggingService.LogInfoAsync(
                            $"[OCR] Gemini Vision fallback model aktif: {model}",
                            "OCR");
                    }

                    return parsedText;
                }

                errors.Add($"model={model}, response kosong");
            }

            throw new InvalidOperationException(
                $"Response Gemini Vision kosong atau semua model gagal. Detail: {string.Join(" | ", errors)}");
        }

        private IEnumerable<string> GetVisionModelCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? candidate in new[] { _visionModel, _fallbackModel })
            {
                if (IsVisionCapableGeminiModel(candidate) && seen.Add(candidate!))
                {
                    yield return candidate!;
                }
            }

            foreach (string candidate in DefaultVisionModelCandidates)
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static bool IsVisionCapableGeminiModel(string? model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) &&
                   model.Contains("flash", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildReceiptParserPrompt(string? vendorType)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Kamu adalah parser OCR untuk struk/faktur pembelian toko sembako Indonesia.");
            prompt.AppendLine("Balas HANYA JSON valid tanpa markdown, tanpa code fence, tanpa penjelasan.");
            prompt.AppendLine();
            prompt.AppendLine("ATURAN:");
            prompt.AppendLine("1. Ekstrak hanya baris produk dan abaikan subtotal, total, kasbon, diskon, pajak, header, footer, catatan pembayaran, dan ringkasan hutang/piutang.");
            prompt.AppendLine("2. supplier_name adalah nama penjual/penerbit faktur pada header atas. buyer_name adalah nama pembeli/pelanggan jika ada.");
            prompt.AppendLine("3. store_name isi sama dengan supplier_name untuk kompatibilitas.");
            prompt.AppendLine("4. Pertahankan nama produk semirip mungkin dengan OCR, tetapi hapus metadata yang jelas bukan nama produk.");
            prompt.AppendLine("5. Metadata yang harus dibuang jika muncul di nama produk: kode SKU panjang di awal, nomor batch/lot seperti 'NB 1701000', dan info kemasan dalam tanda kurung seperti '(RTG,12X10X23 GR)'.");
            prompt.AppendLine("6. Jangan mengganti merek, rasa, ukuran, atau satuan jika tidak yakin.");
            prompt.AppendLine("7. Jangan menggabungkan kata yang sudah terpisah spasi. Contoh: 'Kapal api mix 1Ds' jangan diubah menjadi 'Kapalapimix1Ds'.");
            prompt.AppendLine("8. Format baris bisa bervariasi: [nama][harga][qty][unit][total], [nama][qty][unit][total], atau tabel multi-kolom.");
            prompt.AppendLine("9. Jika quantity ditulis menyatu dengan satuan tanpa spasi seperti '10Box', '5PCS', '2Bks', pisahkan menjadi quantity angka dan unit string.");
            prompt.AppendLine("10. Jika quantity tidak ada, gunakan 1.");
            prompt.AppendLine("11. Jika ada kolom total netto per baris seperti 'Jumlah Setelah Potongan', 'JMLH.BERSIH', atau total bersih lain, gunakan itu sebagai total utama.");
            prompt.AppendLine("12. Jika total netto per baris tersedia, unit_price WAJIB dihitung dari total netto / quantity. Jangan pakai kolom Harga asli karena belum dipotong diskon/pajak.");
            prompt.AppendLine("13. unit adalah satuan seperti pcs, pak, dus, rol, bal, kg, ltr, kmpn, box, bks. Jika tidak ada, gunakan null.");
            prompt.AppendLine("14. Jika nilai tidak ditemukan, gunakan null atau string kosong sesuai tipe field.");
            prompt.AppendLine();
            prompt.AppendLine("Schema wajib:");
            prompt.Append(@"{""supplier_name"":"""",""buyer_name"":"""",""store_name"":"""",""date"":"""",""receipt_number"":"""",""items"":[{""product_name"":"""",""qty_box"":1,""isi_per_box"":null,""quantity"":1,""unit"":null,""unit_price"":0,""total"":0}],""total"":0}");

            string vendorHint = vendorType?.Trim().ToUpperInvariant() switch
            {
                "WINGS_SURAT_JALAN" =>
                    "Hint vendor WINGS_SURAT_JALAN: Ini adalah Surat Jalan dari distributor WINGS / PT. SAYAP MAS UTAMA (SMU). " +
                    "supplier_name WAJIB selalu diisi 'WINGS / PT. SAYAP MAS UTAMA' — ini adalah penerbit dokumen, " +
                    "BUKAN nama di baris PEMBELI, KIRIM KE, kecamatan, atau desa (contoh: 'KEC MANIIS', 'PURWAKAR', 'TK ASIAH'). " +
                    "supplier_name TIDAK BOLEH berupa tanggal atau nomor (contoh: '31.01.1985' adalah nomor SPPKP bukan supplier). " +
                    "buyer_name adalah nama dari baris 'PEMBELI:' saja (contoh: '0110045913 - TK ASIAH'). " +
                    "Format setiap baris produk: [No] [QTY SATUAN KODE] [NAMA BARANG] [SEQ] [ISI] [HARGA] [JMLH.KOTOR] [CUST.DISC] [PROD.DISC] [JMLH.BERSIH]. " +
                    "quantity = angka PERTAMA di kolom kedua. Contoh: '5 BOX 20050'→quantity=5,unit='BOX'. '20 PCS 20145'→quantity=20,unit='PCS'. '12 BOX 20092'→quantity=12,unit='BOX'. '3 BOX 20154'→quantity=3,unit='BOX'. " +
                    "ISI (kolom setelah SEQ) adalah jumlah pcs per box — BUKAN quantity transaksi, jangan dipakai sebagai quantity. " +
                    "total per baris = kolom paling kanan = JMLH.BERSIH (bisa terbaca 'JMLH BERSIH', 'JML BERSIH'). " +
                    "unit_price = JMLH.BERSIH dibagi quantity invoice. Jangan pakai kolom HARGA asli sebagai unit_price karena belum netto. " +
                    "Tanggal faktur ada di header atas kanan, format 'DD.MM.YYYY' (contoh: '14.05.2026', '30.03.2026'). " +
                    "JANGAN ambil tanggal dari baris 'SPPKP: S-27/PKP/KPP.190203/2024 TGL: 31.01.1985' — angka seperti '31.01.1985' adalah tahun pajak, BUKAN tanggal faktur. " +
                    "Tanggal yang benar adalah angka tahun 2025 atau 2026, bukan 1985. " +
                    "Abaikan baris 'LANJUTAN DARI HALAMAN X' karena itu bukan produk. " +
                    "Tambahkan field qty_box dan isi_per_box untuk item Wings. qty_box = angka sebelum BOX/RTG/PCS pada kolom QTY KODE. isi_per_box = angka di kolom ISI untuk kebutuhan konversi stok, BUKAN pembagi harga invoice. " +
                    "quantity tetap quantity invoice dari kolom QTY KODE, unit tetap satuan invoice (BOX, PCS, RTG/RCG, DUS, dll), dan unit_price = JMLH.BERSIH dibagi quantity invoice. " +
                    "Jika ada angka kecil dalam kurung seperti '(2)', itu nomor batch internal, bukan quantity. " +
                    "Contoh: '2 BOX 50102 | Sedaap Minyak 1Lt | 12 | 12 | 285.000 | 491.325' -> qty_box=2, isi_per_box=12, quantity=2, unit='BOX', unit_price=245662.5, total=491325. " +
                    "Contoh: '12 RTG 63251 | Rapika Biang 7Ml Hijab | (2) | 12 | 4.620 | 55.440' -> qty_box=12, isi_per_box=12, quantity=12, unit='RTG', unit_price=4620, total=55440. " +
                    "PENTING — Halaman tanpa produk: Jika halaman hanya berisi 'LANJUTAN DARI HALAMAN X' di atas tabel kosong tanpa baris produk, kembalikan items:[]. " +
                    "is_last_page = true HANYA jika halaman mengandung teks '*** END OF DOCUMENT ***' atau 'END OF DOCUMENT'. Jika tidak ada, is_last_page = false.",
                "FASTRATA_FAKTUR" =>
                    "Hint vendor FASTRATA_FAKTUR: Ini adalah Faktur Penjualan dari PT. FASTRATA BUANA. " +
                    "supplier_name = 'PT. FASTRATA BUANA' (selalu, dari header atas). " +
                    "buyer_name = nama toko dari baris 'Pelanggan :' di header — BUKAN dari baris 'Alamat Kirim' atau 'Alamat Tagih'. " +
                    "PENTING: 'Alamat Kirim' berisi nama jalan/tempat terdekat seperti 'SEBELUM TOKO IDAN' — itu BUKAN nama pembeli. Gunakan HANYA nilai setelah 'Pelanggan :'. " +
                    "Tanggal faktur = nilai dari baris 'Tanggal :' di header kanan atas (contoh '08 May 2026'). " +
                    "JANGAN ambil tanggal dari baris 'Potongan Tambahan OTP', 'Tgl. Cetak', kode referensi, atau catatan footer. " +
                    "Nama produk diawali kode SKU seperti '00J.KPSPM.G0233101XX SP MIX (RTG,12X10X23 GR)' → ambil hanya 'SP MIX', buang kode SKU dan info RTG dalam kurung. " +
                    "Quantity ditulis menyatu dengan satuan: '10Box'→quantity=10,unit='Box'. '5Box'→quantity=5,unit='Box'. '2Bks'→quantity=2,unit='Bks'. " +
                    "Contoh baris lengkap: '1 00J... SP MIX (RTG...) 10Box 200,000 0 0 36,000 1,964,000' → product_name='SP MIX', quantity=10, unit='Box', total=1964000. " +
                    "'2 00J... MOCACINNO (RTG...) 5Box 170,000 8,500 0 15,147 826,353' → product_name='MOCACINNO', quantity=5, unit='Box', total=826353. " +
                    "'3 00J... ABC SUSU (RTG...) 2Box 200,000 6,680 0 7,079 386,241' → product_name='ABC SUSU', quantity=2, unit='Box', total=386241. " +
                    "'4 00J... KA ONE GL PISAH (RTG...) 10Bks 1,355 0 0 0 13,550' → product_name='KA ONE GL PISAH', quantity=10, unit='Bks', total=13550. " +
                    "total per baris = kolom TERAKHIR 'Jumlah Setelah Potongan'. Jika ada harga satuan eksplisit, pakai itu sebagai unit_price. Jika tidak ada, unit_price = total dibagi quantity.",
                "ARTABOGA_FAKTUR" =>
                    "Hint vendor ARTABOGA_FAKTUR: Ini adalah Faktur Tunai PT. Artaboga Cemerlang / Arindo Makmur. " +
                    "supplier_name = 'PT. ARTABOGA CEMERLANG'. " +
                    "Tanggal faktur = nilai dari 'Tgl Faktur'. " +
                    "Format tabel: KODE | NAMA BARANG | BSR | TGH | KCL | HARGA(RP) | BRUTO | DISC1 | DISC2 | JML NETTO. " +
                    "Quantity dipilih dari kolom yang bernilai > 0 dengan prioritas TGH, lalu BSR, lalu KCL. " +
                    "Jika quantity dari TGH gunakan unit='Pak'; jika dari BSR gunakan unit='Dus'; jika dari KCL gunakan unit='Pcs'. " +
                    "total per baris = JML NETTO setelah diskon. Jika kolom HARGA(RP) terbaca, gunakan itu sebagai unit_price. Jika tidak, unit_price = total dibagi quantity. " +
                    "Hapus kode produk numerik di awal nama barang dan pertahankan nama barang utama saja.",
                "TANI_MAKMUR_POS" =>
                    "Hint vendor TANI_MAKMUR_POS: baris pertama adalah supplier_name. Tanggal sering berbentuk 'Wed,06-May-2026,17:27:17'. Baris 'Kasbon' bukan produk dan harus diabaikan. Jika kolom Harga tersedia, pakai langsung sebagai unit_price. Jika qty = 1 dan harga satuan kosong, gunakan total sebagai unit_price.",
                "KASIR_POS_GENERIC" =>
                    "Hint vendor KASIR_POS_GENERIC: Ini struk kasir/POS toko biasa. supplier_name adalah nama toko atau perusahaan di baris pertama header. Tanggal biasanya ada di baris kedua atau ketiga. Format item umumnya [nama produk] [harga_satuan?] [qty] [satuan] [total]. Baris seperti 'Kasbon', 'Total', 'SubTotal', 'Bayar', dan 'Kembalian' bukan produk dan harus diabaikan. Jika ada kolom harga satuan, gunakan itu sebagai unit_price. Jika qty=1 dan harga satuan tidak ada, gunakan total sebagai unit_price.",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(vendorHint))
            {
                prompt.AppendLine();
                prompt.AppendLine();
                prompt.Append(vendorHint);
            }

            return prompt.ToString();
        }

        private static string GetImageMimeType(string imagePath)
        {
            string extension = Path.GetExtension(imagePath)?.ToLowerInvariant() ?? string.Empty;
            return extension switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
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
            var compactHistory = conversationHistory
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => TrimPromptSection(item, 260))
                .TakeLast(4)
                .ToList();

            string contextInfo = compactHistory.Any()
                ? "RIWAYAT TERBARU:\n" + string.Join("\n", compactHistory)
                : "";

            string storeDataInfo = TrimPromptSection(
                realStoreData ?? "Data toko tidak tersedia. Jangan mengarang data.",
                4500);

            string userPrompt = $@"{contextInfo}

{storeDataInfo}

PERTANYAAN USER: {userMessage}

PENTING:
- Gunakan HANYA data toko yang ada di atas.
- Jika pertanyaan pendek seperti 'gimana caranya?', 'yang tadi', atau 'itu berapa?', pakai riwayat percakapan terbaru untuk memahami rujukannya.
- JANGAN mengarang angka, identitas, transaksi, atau fakta apa pun.
- Jika data memang tidak tersedia, jawab jujur dan sarankan command/query relevan seperti /dokumen, /riwayat_restock, /stok, /laporan, /pelanggan, /supplier, atau /piutang.";

            string responsePolicy = $@"ATURAN TAMBAHAN PRIORITAS TINGGI:
1. Nada ringkas dan profesional.
2. Pertanyaan sederhana maksimal 2-4 kalimat.
3. Jangan ulangi pertanyaan user.
4. Jangan tampilkan meta-output seperti 'User:', 'AI:', atau 'Pertanyaan:'.
5. Jika data tidak ada, jawab satu kalimat faktual.
6. Jika lawan bicara kasir meminta data owner-only, jawab: 'Data ini hanya untuk owner.'
7. Jangan menyebut kata 'prompt', 'data real toko', 'data yang disediakan', atau 'simulasi data' di jawaban.";

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
2. Jika data tidak ada di prompt, bilang jujur dan natural: ""Data itu belum ada di sistem saat ini.""
3. Bahasa Indonesia natural, profesional, tapi luwes (seperti asisten toko).
4. JANGAN minta maaf berlebihan. Cukup sekali jika memang perlu.
5. Jawab SINGKAT & PADAT. Langsung ke inti. MAKSIMAL 3-4 kalimat untuk jawaban sederhana.
6. JANGAN gunakan basa-basi seperti 'Halo!', 'Tentu!', 'Baiklah!' di awal kalimat.
7. JANGAN bilang 'Maaf saya tidak bisa' jika Anda bisa menyajikan data dalam bentuk list teks.
8. PROAKTIF: Jika Anda melihat data Stok Minus atau Produk Tidak Laku (Slow Moving) di data yang diberikan, sampaikan sebagai peringatan di awal jawaban Anda.
9. ANTI-HALUSINASI: JANGAN PERNAH mengarang data tabel, angka transaksi, atau riwayat belanja yang tidak ada di prompt. Jika user tanya detail yang tidak ada, bilang ""Data detailnya belum ada di sistem.""
10. JANGAN beri saran/rekomendasi yang tidak diminta user. Jawab sesuai pertanyaan saja.
11. Gunakan riwayat percakapan terbaru hanya untuk memahami rujukan singkat atau follow-up. Jika topik sudah berubah, fokus ke pertanyaan terbaru dan jangan bocorkan data yang tidak relevan.
12. Jika user menanyakan identitas bot, jawab: ""Saya Smart Sembako Assistant (SSA).""
13. Jika user menanyakan kemampuan bot, jawab singkat fitur utama dan arahkan ke /help.
14. Jika user meminta data sangat panjang atau lengkap, sarankan export file CSV bila tersedia. Jangan memaksa menulis daftar panjang di chat.
15. JANGAN gunakan tabel markdown dengan karakter | untuk output chat. Pakai bullet/list teks yang rapi.
16. Jika user menanyakan fitur yang memang belum tersedia, jawab natural. Contoh: ""Fitur itu belum diaktifkan di sistem saat ini.""
17. JANGAN menyebut proses internal seperti prompt, data simulasi, atau data yang disediakan.

KEMAMPUAN ANALISIS & LAPORAN:
- **Stok:** Bisa analisa stok aman, rendah, habis, dan MINUS.
- **Pelanggan:** Bisa sebutkan nama pelanggan loyal (yang sering belanja/belanja banyak).
- **Laba/Rugi:** Hitung (Harga Jual - Harga Modal) * Terjual. Jika Modal 0, katakan 'Belum diinput'.
- **Tren:** Bandingkan hari ini vs kemarin.
- **Tampilan Data:** Jika user minta 'daftar', 'data', atau 'laporan', tampilkan sebagai bullet/list teks ringkas. Hindari tabel markdown.
- **Riwayat Pelanggan:** Jika ada data riwayat belanja pelanggan di prompt, gunakan itu. JANGAN mengarang transaksi.
- **Data Total:** Jika prompt memuat ""Total pelanggan terdaftar"", ""Total supplier"", atau ""Total produk"", gunakan angka itu saat user bertanya jumlah/count.

CONTOH JAWABAN YANG BENAR:
✅ User: ""Stok kapal api berapa?""
✅ AI: ""Stok Kapal Api Mix: -6 Rcg (Minus).""

✅ User: ""Wawan belanja apa aja?""
✅ AI: ""Data riwayat belanja Wawan tidak tersedia di sistem."" (JIKA DATA TIDAK ADA DI PROMPT)

✅ User: ""Tampilkan data stok minus""
✅ AI: ""Berikut data produk dengan stok minus:
- Roti: -10
- Kopi: -4""

❌ JAWABAN SALAH (HALUSINASI):
❌ ""Wawan telah melakukan 22 kali belanja dengan total Rp 8.203.150. Berikut rinciannya: [tabel karangan]""

{(userRole == "Owner" ? "- Fokus insight bisnis, profit, & strategi" : "- Fokus stok & transaksi")}";

            string compactSystemPrompt = $@"Anda adalah Smart Sembako Assistant (SSA), asisten operasional toko sembako.
Peran lawan bicara: {userRole ?? "Guest"}.
Database yang tersedia:
- pos.db Aronium POS: produk, stok, transaksi, dokumen, pelanggan, supplier, kasir.
- memory.db: riwayat chat dan log bot.
Kemampuan utama:
- Baca stok produk, laporan penjualan, dokumen restock/penjualan, pelanggan, supplier, piutang, kasir.
- Analisa produk terlaris, slow moving, dead stock, rekomendasi restock, profit margin.
Batas data:
- Tanggal expired tidak selalu tersedia sebagai data produk terstruktur; bila tidak ada, sarankan cek dokumen pembelian/restock terakhir.
- Kategori produk tidak selalu terstruktur; gunakan keyword matching dari nama/kategori produk.
- Foto/gambar produk tidak tersedia dari database.
Aturan:
1. Jawab singkat, langsung, bahasa Indonesia natural.
2. Gunakan hanya data toko yang diberikan di pesan user. Jangan mengarang angka, transaksi, stok, harga, pelanggan, atau supplier.
3. Stok minus harus disebut minus, bukan nol.
4. Kasir tidak boleh menerima profit, harga modal, atau data owner-only; jawab 'Data ini hanya untuk owner.'
5. Jika user meminta data lengkap/panjang, sarankan export CSV/ZIP bila tersedia.
6. Jangan pakai tabel markdown dengan karakter |; gunakan bullet teks.
7. Bedakan jelas: slow moving = stok masih ada dan volume jual rendah; dead stock = tidak terjual >14 hari; stok minus = oversold/perlu koreksi/restock.

=== DEFINISI STOK (WAJIB DIPAKAI) ===

SLOW MOVING (Layer A):
Masih terjual tapi < 40% rata-rata kategorinya per 30 hari.
Stok bisa + atau -. Jika stok minus: label ""stok perlu koreksi data"".
Command: /slow_moving

DEAD STOCK (Layer B):
Stok > 0, tidak terjual > 21 hari, bukan baru restock (< 14 hari),
bukan kategori mandatory, bukan unit besar dengan unit turunan yang laku.
Command: /dead_stock

SLEEPING MANDATORY (Layer C):
Sold30d <= 3 ATAU tidak terjual > 21 hari, tapi kategori wajib ada
(Obat, Obat Nyamuk, Sembako, Perlengkapan Bayi, Makanan Bayi).
JANGAN sarankan hapus atau retur. Saran: pertahankan, cek expired.
Command: /sleeping_stock

OVERSOLD / STOK MINUS:
Stok negatif = produk SANGAT LAKU atau data perlu opname.
JANGAN label sebagai tidak laku atau dead stock.
Contoh: Roti@2000 stok=-7668 -> terlaris, bukan dead stock.

SHADOW STOCK:
Sebelum label dead stock pada produk Dus/Pak/Krat:
-> Cek unit turunan. Jika masih laku: ""unit besar lambat, keluarga bergerak""
-> Jika mapping belum ada: beri peringatan, jangan simpulkan langsung.

PRODUK BARU:
Baru restock < 14 hari -> BUKAN dead stock.

PROFIT:
Margin > 40% kemungkinan karena banyak produk tidak ada Cost.
Selalu sertakan catatan ""X% omzet tidak ada data modal"" pada laporan profit bila datanya tersedia.

DATA EXPIRED:
Jika produk tidak punya data expired, jawab ""tidak ada data expired untuk produk ini di sistem.""";

            string rawResponse = await SendPromptAsync(compactSystemPrompt, userPrompt, 0.4, 700);
            return SanitizeAssistantResponse(rawResponse);
        }

        private static string TrimPromptSection(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxChars)
            {
                return value ?? string.Empty;
            }

            return value[..maxChars].TrimEnd() + "\n...data dipotong; minta export jika perlu daftar lengkap.";
        }

        private static string SanitizeAssistantResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return "Data tidak tersedia di sistem.";
            }

            var lines = response
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line =>
                    !line.StartsWith("User:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("AI:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Pertanyaan:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("PERTANYAAN USER:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("✅ User:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("✅ AI:", StringComparison.OrdinalIgnoreCase))
                .Select(CollapseDuplicateSentences)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            string cleaned = string.Join(
                Environment.NewLine,
                lines.Where((line, index) => index == 0 || !string.Equals(line, lines[index - 1], StringComparison.OrdinalIgnoreCase)))
                .Trim();
            if (cleaned.StartsWith("AI:", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[3..].Trim();
            }

            return string.IsNullOrWhiteSpace(cleaned)
                ? "Data tidak tersedia di sistem."
                : cleaned;
        }

        private static string CollapseDuplicateSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var matches = Regex.Matches(text, @"[^.!?]+[.!?]*");
            if (matches.Count == 0)
            {
                return text.Trim();
            }

            var builder = new StringBuilder();
            string? previous = null;

            foreach (Match match in matches)
            {
                string sentence = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                string normalized = Regex.Replace(sentence, @"\s+", " ");
                if (string.Equals(normalized, previous, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(sentence);
                previous = normalized;
            }

            return builder.ToString().Trim();
        }

        private static bool LooksLikeStructuredJson(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            string trimmed = response.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                trimmed = Regex.Replace(trimmed, "^```(?:json)?|```$", string.Empty, RegexOptions.Multiline).Trim();
            }

            if (!trimmed.StartsWith("{", StringComparison.Ordinal) &&
                !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
