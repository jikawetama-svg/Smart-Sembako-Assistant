using System.Text;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class CommandHandler
    {
        private readonly GroqService _groqService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;

        public CommandHandler(
            GroqService groqService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService = null)
        {
            _groqService = groqService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
        }

        public async Task<string> HandleCommandAsync(string command, string userId, string channel, bool isOwner = true)
        {
            string normalized = command.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Perintah kosong.";
            }

            try
            {
                string[] parts = normalized.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLowerInvariant();
                string args = parts.Length > 1 ? parts[1] : string.Empty;

                return cmd switch
                {
                    "/start" or "/help" => BuildHelpMessage(isOwner),
                    "/stok" => await HandleStockCommandAsync(args),
                    "/laporan" => await HandleReportCommandAsync(isOwner),
                    _ => await HandleNaturalLanguageAsync(normalized, userId, channel, isOwner)
                };
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Command handler error: {ex.Message}", "CommandHandler", ex.ToString(), userId);
                return "Terjadi kesalahan saat memproses perintah.";
            }
        }

        private async Task<string> HandleStockCommandAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var products = await _posDbService.GetAllProductsAsync();
            if (string.IsNullOrWhiteSpace(args))
            {
                var lowStock = products.Where(p => (p.Stock ?? 0) <= 10).Take(10).ToList();
                if (!lowStock.Any())
                {
                    return "Semua stok aman.";
                }

                return "Stok rendah:\n" + string.Join("\n", lowStock.Select(p => $"- {p.Name}: {p.Stock} {p.Unit}"));
            }

            string query = args.Trim();
            var matches = products
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) &&
                            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();

            if (!matches.Any())
            {
                return $"Produk \"{query}\" tidak ditemukan.";
            }

            string? familyResponse = await TryBuildFamilyStockResponseAsync(query, matches, products);
            if (!string.IsNullOrWhiteSpace(familyResponse))
            {
                return familyResponse;
            }

            return "Hasil pencarian stok:\n" + string.Join("\n", matches.Select(p => $"- {p.Name}: {p.Stock} {p.Unit}"));
        }

        private async Task<string> HandleReportCommandAsync(bool isOwner)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var breakdown = await _posDbService.GetSalesProfitBreakdownAsync(DateTime.Today, DateTime.Today);

            return isOwner
                ? $"Laporan hari ini\nRevenue setelah diskon: {FormatCurrency(breakdown.Revenue)}\nModal: {FormatCurrency(breakdown.Cost)}\nProfit Aronium: {FormatCurrency(breakdown.AroniumProfit)}\nTransaksi: {breakdown.TransactionCount}"
                : $"Laporan hari ini\nRevenue: {FormatCurrency(breakdown.Revenue)}\nTransaksi: {breakdown.TransactionCount}";
        }

        private async Task<string> HandleNaturalLanguageAsync(string message, string userId, string channel, bool isOwner)
        {
            if (_posDbService != null && isOwner)
            {
                string? deterministicResponse = await TryHandleProfitDiscountTaxIntentAsync(message);
                if (!string.IsNullOrWhiteSpace(deterministicResponse))
                {
                    return deterministicResponse;
                }
            }

            long chatId = long.TryParse(userId, out var parsed) ? parsed : 0;
            var history = await _databaseService.GetRecentConversationsAsync(chatId, 5);
            var historyTexts = history.Select(h => $"{h.Role}: {h.Message}").ToList();
            string context = _posDbService == null ? "Data toko tidak tersedia." : $"Role user: {(isOwner ? "Owner" : "Kasir")}";

            string response = await _groqService.GenerateNaturalResponseAsync(
                message,
                historyTexts,
                isOwner ? "Owner" : "Kasir",
                context);

            await _databaseService.AddConversationAsync(new Conversation
            {
                ChatId = chatId,
                UserName = userId,
                Role = "assistant",
                Message = response,
                MessageType = "text",
                Timestamp = DateTime.Now
            });

            return response;
        }

        private async Task<string?> TryHandleProfitDiscountTaxIntentAsync(string message)
        {
            if (_posDbService == null)
            {
                return null;
            }

            string normalized = message.ToLowerInvariant();
            bool mentionsProfit = ContainsAny(normalized, "profit", "laba", "untung", "keuntungan", "margin", "omzet");
            bool mentionsFormula = ContainsAny(normalized, "cara hitung", "rumus", "dihitung", "ngitung", "mekanisme", "beda sama omzet", "termasuk diskon");
            bool mentionsPromo = ContainsAny(normalized, "promo", "diskon", "gratis");
            bool mentionsTax = ContainsAny(normalized, "pajak", "qris", "admin");
            bool mentionsNegative = ContainsAny(normalized, "minus", "rugi", "kecil");

            if (mentionsTax && ContainsAny(normalized, "aktif", "ada", "berapa", "profit setelah", "hari ini"))
            {
                var (start, end) = ResolveSimplePeriod(normalized);
                var taxes = await _posDbService.GetActiveTaxesAsync();
                var breakdown = await _posDbService.GetSalesProfitBreakdownAsync(start, end);
                return BuildTaxAnswer(taxes, breakdown);
            }

            if (mentionsPromo)
            {
                string? productQuery = ExtractProductQuery(message, "promo", "diskon", "gratis", "produk", "apa", "saja", "yang", "lagi", "aktif");
                var promotions = string.IsNullOrWhiteSpace(productQuery) || ContainsAny(normalized, "aktif", "produk apa", "apa saja")
                    ? await _posDbService.GetActivePromotionsAsync(DateTime.Today)
                    : await _posDbService.GetProductPromotionStatusAsync(productQuery, DateTime.Today);
                return BuildPromotionAnswer(promotions, productQuery);
            }

            if (mentionsProfit || mentionsFormula || mentionsNegative)
            {
                var (start, end) = ResolveSimplePeriod(normalized);
                string? productQuery = mentionsNegative
                    ? ExtractProductQuery(message, "kenapa", "profit", "minus", "rugi", "kecil", "produk")
                    : null;
                var context = await _posDbService.GetProfitExplanationContextAsync(start, end, productQuery);
                return BuildProfitAnswer(context, mentionsFormula, mentionsTax, productQuery);
            }

            return null;
        }

        private static string BuildProfitAnswer(
            ProfitExplanationContext context,
            bool includeFormula,
            bool afterTax,
            string? productQuery)
        {
            if (!string.IsNullOrWhiteSpace(productQuery) && context.NotableCases.Any())
            {
                var item = context.NotableCases[0];
                var sbProduct = new StringBuilder();
                sbProduct.AppendLine($"{item.ProductName}:");
                sbProduct.AppendLine($"Total setelah diskon: {FormatCurrency(item.RevenueAfterDiscount)}");
                sbProduct.AppendLine($"Modal: {FormatCurrency(item.Cost)}");
                sbProduct.AppendLine($"Profit: {FormatCurrency(item.Profit)}");
                if (!string.IsNullOrWhiteSpace(item.PromotionName))
                {
                    sbProduct.AppendLine($"Promo terkait: {item.PromotionName}");
                }
                sbProduct.Append(item.Explanation);
                return sbProduct.ToString();
            }

            if (includeFormula)
            {
                return PosDbService.BuildPlainLanguageProfitExplanation(context);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Profit: {FormatCurrency(context.AroniumProfit)}");
            sb.AppendLine($"Total penjualan setelah diskon: {FormatCurrency(context.RevenueAfterDiscount)}");
            sb.AppendLine($"Modal barang: {FormatCurrency(context.CostOfGoodsSold)}");

            if (context.ItemDiscountAmount != 0 || context.DocumentDiscountAmount != 0)
            {
                sb.AppendLine($"Diskon item: {FormatCurrency(context.ItemDiscountAmount)}");
                sb.AppendLine($"Diskon nota: {FormatCurrency(context.DocumentDiscountAmount)}");
            }

            if (context.TaxAmount != 0 || afterTax)
            {
                sb.AppendLine($"Pajak/biaya transaksi: {FormatCurrency(context.TaxAmount)}");
                sb.AppendLine($"Profit setelah pajak/biaya: {FormatCurrency(context.ProfitAfterTax)}");
            }

            sb.Append("Angka profit default mengikuti Aronium: total setelah diskon dikurangi modal barang.");
            return sb.ToString();
        }

        private static string BuildPromotionAnswer(List<ActivePromotionInfo> promotions, string? productQuery)
        {
            if (!promotions.Any())
            {
                return string.IsNullOrWhiteSpace(productQuery)
                    ? "Tidak ada promo aktif yang terbaca dari Aronium."
                    : $"Tidak ada promo aktif untuk \"{productQuery}\" di Aronium.";
            }

            if (!string.IsNullOrWhiteSpace(productQuery))
            {
                var promo = promotions[0];
                string period = $"{promo.StartDate:dd/MM/yyyy} sampai {promo.EndDate:dd/MM/yyyy}";
                return $"{promo.ProductName} sedang ikut promo {promo.PromotionName}.\nAturannya: {promo.HumanReadableRule}.\nPeriode: {period}.";
            }

            var grouped = promotions.GroupBy(p => p.PromotionName).OrderBy(g => g.Key);
            var sb = new StringBuilder();
            sb.AppendLine("Promo aktif di Aronium:");
            foreach (var group in grouped)
            {
                sb.AppendLine($"- {group.Key}: {group.First().HumanReadableRule}");
                foreach (var item in group.Take(8))
                {
                    sb.AppendLine($"  - {item.ProductName}");
                }
                if (group.Count() > 8)
                {
                    sb.AppendLine($"  - +{group.Count() - 8} produk lain");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildTaxAnswer(List<ActiveTaxInfo> taxes, SalesProfitBreakdown breakdown)
        {
            var sb = new StringBuilder();
            if (!taxes.Any())
            {
                sb.AppendLine("Tidak ada pajak/biaya aktif yang terbaca dari Aronium.");
            }
            else
            {
                sb.AppendLine("Pajak/biaya aktif di Aronium:");
                foreach (var tax in taxes)
                {
                    sb.AppendLine($"- {tax.Name}: {tax.HumanReadableRule}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Yang benar-benar masuk transaksi periode ini: {FormatCurrency(breakdown.TaxAmount)}.");
            sb.AppendLine($"Profit sesuai Aronium: {FormatCurrency(breakdown.AroniumProfit)}.");
            sb.Append($"Profit setelah pajak/biaya: {FormatCurrency(breakdown.ProfitAfterTax)}.");
            return sb.ToString();
        }

        private static (DateTime Start, DateTime End) ResolveSimplePeriod(string normalized)
        {
            DateTime today = DateTime.Today;
            if (ContainsAny(normalized, "bulan ini", "bulanan"))
            {
                return (new DateTime(today.Year, today.Month, 1), today);
            }

            if (ContainsAny(normalized, "minggu ini", "pekan ini", "weekly"))
            {
                int offset = ((int)today.DayOfWeek + 6) % 7;
                return (today.AddDays(-offset), today);
            }

            return (today, today);
        }

        private static string? ExtractProductQuery(string message, params string[] wordsToRemove)
        {
            string result = message;
            foreach (string word in wordsToRemove)
            {
                result = result.Replace(word, "", StringComparison.OrdinalIgnoreCase);
            }

            result = result
                .Replace("?", "", StringComparison.Ordinal)
                .Replace(":", "", StringComparison.Ordinal)
                .Trim();

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatCurrency(decimal value)
        {
            return $"Rp {value:N0}";
        }

        private string BuildHelpMessage(bool isOwner)
        {
            return isOwner
                ? "Command: /stok, /laporan, /pelanggan, /supplier, /user, /penjualan, /dokumen, /restock, /inventory, /analisa, /help"
                : "Command: /stok, /laporan, /help";
        }

        private async Task<string?> TryBuildFamilyStockResponseAsync(string query, List<Product> matches, List<Product> allProducts)
        {
            var matchedIds = matches
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .Select(product => product.Id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (matchedIds.Count == 0)
            {
                return null;
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var mapping = mappings.FirstOrDefault(item =>
                matchedIds.Contains(item.ParentProductId) ||
                matchedIds.Contains(item.ChildProductId));
            if (mapping == null)
            {
                return null;
            }

            var productById = allProducts
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .GroupBy(product => product.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            if (!productById.TryGetValue(mapping.ParentProductId, out var parent) ||
                !productById.TryGetValue(mapping.ChildProductId, out var child))
            {
                return null;
            }

            var family = new ProductFamilyStock
            {
                Mapping = mapping,
                ParentProduct = parent,
                ChildProduct = child,
                ParentStock = parent.Stock ?? 0,
                ChildStock = child.Stock ?? 0,
                ConversionRate = mapping.ConversionRate
            };

            var response = GroqService.FormatDualStockResponse(family, query);
            var otherMatches = matches
                .Where(product => !string.Equals(product.Id, mapping.ParentProductId, StringComparison.OrdinalIgnoreCase) &&
                                  !string.Equals(product.Id, mapping.ChildProductId, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();
            if (!otherMatches.Any())
            {
                return response;
            }

            return response + "\n\nProduk lain:\n" +
                   string.Join("\n", otherMatches.Select(product => $"- {product.Name}: {product.Stock} {product.Unit}"));
        }
    }
}
