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

            return "Hasil pencarian stok:\n" + string.Join("\n", matches.Select(p => $"- {p.Name}: {p.Stock} {p.Unit}"));
        }

        private async Task<string> HandleReportCommandAsync(bool isOwner)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            decimal revenue = await _posDbService.GetTodayRevenueAsync();
            decimal profit = await _posDbService.GetTodayProfitAsync();
            int transactionCount = await _posDbService.GetSalesTransactionCountAsync(DateTime.Today, DateTime.Today);

            return isOwner
                ? $"Laporan hari ini\nRevenue: Rp {revenue:N0}\nProfit: Rp {profit:N0}\nTransaksi: {transactionCount}"
                : $"Laporan hari ini\nRevenue: Rp {revenue:N0}\nTransaksi: {transactionCount}";
        }

        private async Task<string> HandleNaturalLanguageAsync(string message, string userId, string channel, bool isOwner)
        {
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

        private string BuildHelpMessage(bool isOwner)
        {
            return isOwner
                ? "Command: /stok, /laporan, /pelanggan, /supplier, /user, /penjualan, /dokumen, /restock, /inventory, /analisa, /help"
                : "Command: /stok, /laporan, /help";
        }
    }
}
