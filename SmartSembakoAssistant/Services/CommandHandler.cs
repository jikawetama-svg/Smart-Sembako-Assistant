using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
            try
            {
                // Parse command
                var parts = command.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "Perintah tidak dikenali.";

                string cmd = parts[0].ToLower();
                string args = parts.Length > 1 ? parts[1] : "";

                // Route to appropriate handler with role check
                return cmd switch
                {
                    "/start" or "/help" => HandleHelpCommand(isOwner),
                    "/stok" => await HandleStokCommandAsync(args.Split(' '), userId, channel),
                    "/laporan" => await HandleLaporanCommandAsync(userId, channel, isOwner),
                    "/restock" => await HandleRestockCommandAsync(args.Split(' '), userId, channel, isOwner),
                    "/inventory" or "/quick_inventory" => isOwner ? await HandleInventoryCommandAsync(args.Split(' '), userId, channel) : "⛔ Akses Ditolak: Fitur Quick Inventory hanya untuk Owner.",
                    "/analisa" => isOwner ? await HandleAnalisaCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Analisa hanya untuk Owner.",
                    "/cek_modal" => isOwner ? await HandleCekModalCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Cek Modal hanya untuk Owner.",
                    "/laporan_kasir" => isOwner ? await HandleLaporanKasirCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Laporan Kasir hanya untuk Owner.",
                    "/dead_stock" => isOwner ? await HandleDeadStockCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Dead Stock hanya untuk Owner.",
                    "/riwayat_restock" => isOwner ? await HandleRestockHistoryCommandAsync(args, userId, channel) : "⛔ Akses Ditolak: Fitur Riwayat Restock hanya untuk Owner.",
                    "/riwayat_inventory" => isOwner ? await HandleInventoryHistoryCommandAsync(args, userId, channel) : "⛔ Akses Ditolak: Fitur Riwayat Inventory hanya untuk Owner.",
                    "/rekomendasi_restock" => isOwner ? await HandleAutoRestockRecommendationCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Rekomendasi Restock hanya untuk Owner.",
                    "/notifikasi_stok" => isOwner ? await HandleStockNotificationCommandAsync(userId, channel) : "⛔ Akses Ditolak: Fitur Notifikasi Stok hanya untuk Owner.",
                    _ => await HandleNaturalLanguageAsync(command, userId, channel, isOwner)
                };
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error handling command: {ex.Message}", channel, ex.ToString());
                return "Terjadi kesalahan. Silakan coba lagi.";
            }
        }

        private async Task<string> HandleStokCommandAsync(string[] args, string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            string searchQuery = args.Length > 0 ? string.Join(" ", args) : "";

            if (!string.IsNullOrEmpty(searchQuery))
            {
                // Search produk
                var products = await _posDbService.GetAllProductsAsync();
                var filtered = products.Where(p =>
                    p.Name != null && p.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .ToList();

                if (filtered.Any())
                {
                    string message = "📦 **Hasil Pencarian Stok:**\n\n";
                    foreach (var product in filtered)
                    {
                        string status = product.Stock <= 5 ? "🔴" : product.Stock <= 10 ? "🟡" : "🟢";
                        message += $"{status} {product.Name}: {product.Stock} {product.Unit}\n";
                    }
                    return message;
                }
                else
                {
                    return $"❌ Produk dengan nama \"{searchQuery}\" tidak ditemukan.";
                }
            }
            else
            {
                // Tampilkan stok rendah
                var lowStock = await _posDbService.GetLowStockProductsAsync(20);

                if (lowStock.Any())
                {
                    string message = "⚠️ **Stok Rendah:**\n\n";
                    foreach (var product in lowStock.Take(10))
                    {
                        string status = product.Stock <= 5 ? "🔴" : "🟡";
                        message += $"{status} {product.Name}: {product.Stock} {product.Unit}\n";
                    }
                    return message;
                }
                else
                {
                    return "✅ Semua stok aman!";
                }
            }
        }

        private async Task<string> HandleLaporanCommandAsync(string userId, string channel, bool isOwner)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                var revenue = await _posDbService.GetTodayRevenueAsync();
                var profit = await _posDbService.GetTodayProfitAsync();
                var transactions = await _posDbService.GetRecentTransactionsAsync(5);

                string message = $@"📊 **Laporan Hari Ini**

💰 Revenue: Rp {revenue:N0}";
                
                if (isOwner)
                {
                    message += $@"
📈 Profit: Rp {profit:N0}";
                }

                message += $@"
🧾 Jumlah Transaksi: {transactions.Count}

{(transactions.Any() ? "🕐 Transaksi Terakhir:\n" + string.Join("\n", transactions.Take(3).Select(t => $"- {t.Date:HH:mm} - Rp {t.Total:N0}")) : "")}";

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating laporan: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil laporan.";
            }
        }

        private async Task<string> HandleRestockCommandAsync(string[] args, string userId, string channel, bool isOwner)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                string fullArgs = string.Join(" ", args);

                // Cek apakah ini bulk restock (format: produk1 qty1 harga1, produk2 qty2 harga2)
                if (fullArgs.Contains(','))
                {
                    return await HandleBulkRestockCommandAsync(fullArgs, userId, channel, isOwner);
                }

                // Format: /restock <produk> <qty> [harga_modal]
                if (args.Length < 2)
                    return "⚠️ Format: /restock <produk> <qty> [harga_modal]\nContoh: /restock minyak 50 14000";

                string productName = string.Join(" ", args.Take(args.Length - 1));
                string qtyStr = args[args.Length - 1];
                string? priceStr = args.Length >= 3 ? args[args.Length - 2] : null;

                if (!int.TryParse(qtyStr, out int qty) || qty <= 0)
                    return "⚠️ Quantity harus berupa angka positif.";

                decimal? price = null;
                if (priceStr != null && !decimal.TryParse(priceStr, out decimal p))
                    return "⚠️ Harga modal harus berupa angka.";
                else if (priceStr != null)
                    price = p;

                // Cari produk
                var products = await _posDbService.GetAllProductsAsync();
                var product = products.FirstOrDefault(p =>
                    p.Name != null && p.Name.Contains(productName, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                    return $"❌ Produk \"{productName}\" tidak ditemukan.";

                // Hitung rekomendasi
                var recommendation = await _posDbService.GetRestockRecommendationAsync(product.Id);

                string message = $"📦 **Rekomendasi Restock: {product.Name}**\n\n";
                message += $"📊 Stok Saat Ini: {product.Stock} {product.Unit}\n";
                message += $"📈 Rata-rata Penjualan: {recommendation.AverageSales:F1} {product.Unit}/hari\n";
                message += $"⏰ Hari Aman: {recommendation.DaysSafe} hari\n";
                message += $"🎯 Rekomendasi: Restock {recommendation.RecommendedQty} {product.Unit}\n";

                if (price.HasValue)
                {
                    message += $"💰 Harga Modal: Rp {price:N0}\n";
                    message += $"💵 Total Biaya: Rp {(price.Value * recommendation.RecommendedQty):N0}\n";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating restock: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat memproses restock.";
            }
        }

        private async Task<string> HandleBulkRestockCommandAsync(string args, string userId, string channel, bool isOwner)
        {
            // Format: produk1 qty1 harga1, produk2 qty2 harga2
            var items = args.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var results = new List<string>();

            foreach (var item in items)
            {
                var parts = item.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string productName = string.Join(" ", parts.Take(parts.Length - 1));
                string qtyStr = parts[parts.Length - 1];
                string? priceStr = parts.Length >= 3 ? parts[parts.Length - 2] : null;

                if (!int.TryParse(qtyStr, out int qty)) continue;

                decimal? price = null;
                if (priceStr != null && decimal.TryParse(priceStr, out decimal p)) price = p;

                // Cari produk dan generate rekomendasi
                var products = await _posDbService.GetAllProductsAsync();
                var product = products.FirstOrDefault(p =>
                    p.Name != null && p.Name.Contains(productName, StringComparison.OrdinalIgnoreCase));

                if (product != null)
                {
                    var recommendation = await _posDbService.GetRestockRecommendationAsync(product.Id);
                    results.Add($"{product.Name}: Rekomendasi {recommendation.RecommendedQty} {product.Unit}");
                }
            }

            if (results.Any())
            {
                return "📦 **Rekomendasi Bulk Restock:**\n\n" + string.Join("\n", results);
            }
            else
            {
                return "❌ Tidak ada produk yang valid untuk restock.";
            }
        }

        private async Task<string> HandleInventoryCommandAsync(string[] args, string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            if (args.Length < 2)
                return "⚠️ Format: /inventory <produk> <qty>\nContoh: /inventory minyak 50";

            try
            {
                string productName = string.Join(" ", args.Take(args.Length - 1));
                string qtyStr = args[args.Length - 1];

                if (!decimal.TryParse(qtyStr, out decimal newStock))
                    return "⚠️ Quantity harus berupa angka.";

                // Cari produk
                var products = await _posDbService.GetAllProductsAsync();
                var product = products.FirstOrDefault(p =>
                    p.Name != null && p.Name.Contains(productName, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                    return $"❌ Produk \"{productName}\" tidak ditemukan.";

                decimal oldStock = product.Stock ?? 0;
                decimal adjustment = newStock - oldStock;

                // Update stok
                await _posDbService.UpdateProductStockAsync(product.Id, newStock);

                // Log inventory adjustment
                await _databaseService.AddInventoryLogAsync(new InventoryLog
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    OldStock = oldStock,
                    NewStock = newStock,
                    Adjustment = adjustment,
                    Reason = "Quick Inventory",
                    UserId = userId,
                    Channel = channel,
                    Timestamp = DateTime.Now
                });

                string status = adjustment > 0 ? "📈 Ditambah" : adjustment < 0 ? "📉 Dikurangi" : "📊 Diset";
                return $"✅ **Stok Updated: {product.Name}**\n\n" +
                       $"📦 Stok Lama: {oldStock} {product.Unit}\n" +
                       $"📦 Stok Baru: {newStock} {product.Unit}\n" +
                       $"{status}: {Math.Abs(adjustment)} {product.Unit}";
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error updating inventory: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat update inventory.";
            }
        }

        private async Task<string> HandleAnalisaCommandAsync(string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                // Ambil data untuk analisa
                var todayRevenue = await _posDbService.GetTodayRevenueAsync();
                var todayProfit = await _posDbService.GetTodayProfitAsync();
                var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
                var yesterdayProfit = await _posDbService.GetYesterdayProfitAsync();
                var weekRevenue = await _posDbService.GetWeekRevenueAsync();
                var monthRevenue = await _posDbService.GetMonthRevenueAsync();

                var topSelling = await _posDbService.GetTopSellingProductsAsync(5);
                var lowStock = await _posDbService.GetLowStockProductsAsync(5);
                var deadStock = await _posDbService.GetDeadStockProductsAsync();
                var zeroCostProducts = await _posDbService.GetZeroCostProductsAsync();

                var allProducts = await _posDbService.GetAllProductsAsync();
                int totalProducts = allProducts.Count;
                int safeStock = allProducts.Count(p => p.Stock > 10);
                int lowStockCount = allProducts.Count(p => p.Stock > 0 && p.Stock <= 10);
                int outOfStock = allProducts.Count(p => p.Stock <= 0);

                string message = $"📊 **ANALISA BISNIS LENGKAP**\n\n";

                // Revenue & Profit
                message += $"💰 **PENDAPATAN & PROFIT**\n";
                message += $"Hari Ini: Rp {todayRevenue:N0} | Profit: Rp {todayProfit:N0}\n";
                message += $"Kemarin: Rp {yesterdayRevenue:N0} | Profit: Rp {yesterdayProfit:N0}\n";
                message += $"Minggu Ini: Rp {weekRevenue:N0}\n";
                message += $"Bulan Ini: Rp {monthRevenue:N0}\n\n";

                // Produk Terlaris
                if (topSelling.Any())
                {
                    message += $"🔥 **PRODUK TERLARIS**\n";
                    foreach (var p in topSelling.Take(3))
                    {
                        message += $"{p.Name}: {p.SoldThisMonth} terjual\n";
                    }
                    message += "\n";
                }

                // Status Stok
                message += $"📦 **STATUS STOK**\n";
                message += $"Total Produk: {totalProducts}\n";
                message += $"🟢 Aman (>10): {safeStock}\n";
                message += $"🟡 Rendah (1-10): {lowStockCount}\n";
                message += $"🔴 Habis (≤0): {outOfStock}\n\n";

                // Stok Rendah
                if (lowStock.Any())
                {
                    message += $"⚠️ **STOK RENDAH**\n";
                    foreach (var p in lowStock.Take(3))
                    {
                        message += $"{p.Name}: {p.Stock} {p.Unit}\n";
                    }
                    message += "\n";
                }

                // Dead Stock
                if (deadStock.Any())
                {
                    message += $"💀 **DEAD STOCK** ({deadStock.Count} produk)\n";
                    message += "Produk yang tidak terjual >14 hari\n\n";
                }

                // Produk Tanpa Modal
                if (zeroCostProducts.Any())
                {
                    message += $"⚠️ **PRODUK TANPA MODAL** ({zeroCostProducts.Count} produk)\n";
                    message += "Perlu update harga modal untuk analisa akurat\n";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating analisa: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil data analisa.";
            }
        }

        private string HandleHelpCommand()
        {
            return @"Bantuan Bot Smart Sembako Assistant:

🤖 Command Cepat:
/stok [nama] - Cek stok produk
/laporan - Laporan hari ini
/restock [produk] [qty] [harga] - Restock produk
/inventory [produk] [qty] - Koreksi stok
/analisa - Analisa bisnis
/help - Bantuan ini

💬 Chat Natural:
Ketik pertanyaan bebas seperti:
- Stok beras berapa?
- Gimana penjualan hari ini?

📄 OCR Struk:
Kirim foto struk untuk parsing otomatis.

Kirim /help kapan saja untuk bantuan.";
        }

        private async Task<string> HandleNaturalLanguageAsync(string message, string userId, string channel)
        {
            // Use Groq for natural language
            if (_groqService == null) return "AI service tidak tersedia.";

            // Get conversation history
            var history = await _databaseService.GetRecentConversationsAsync(userId, 10);

            // Call Groq
            var response = await _groqService.ChatAsync(message, history, channel);

            // Save to memory
            await _databaseService.AddConversationAsync(new Conversation
            {
                ChatId = long.Parse(userId),
                UserName = "User",
                Role = "user",
                Message = message,
                MessageType = "text"
            });

            await _databaseService.AddConversationAsync(new Conversation
            {
                ChatId = long.Parse(userId),
                UserName = "Bot",
                Role = "assistant",
                Message = response,
                MessageType = "text"
            });

            return response;
        }

        private async Task<string> HandleLaporanKasirCommandAsync(string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                var salesPerUser = await _posDbService.GetSalesPerUserAsync();

                if (!salesPerUser.Any())
                {
                    return "ℹ️ **Belum ada data transaksi hari ini.**";
                }

                string message = "🧑‍ **LAPORAN PENJUALAN PER KASIR**\n\n";
                message += "| Kasir | Transaksi | Total Penjualan |\n";
                message += "|---|---|---|\n";

                foreach (var s in salesPerUser)
                {
                    message += $"| {s.Name} | {s.TransactionCount}x | Rp {s.TotalSales:N0} |\n";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating laporan kasir: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil data kasir.";
            }
        }

        private async Task<string> HandleDeadStockCommandAsync(string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                var deadStock = await _posDbService.GetDeadStockProductsAsync();

                if (!deadStock.Any())
                {
                    return "✅ **Tidak ada Dead Stock!**\n\nSemua produk dengan stok terjual dalam 14 hari terakhir.";
                }

                string message = "💀 **DEAD STOCK (Tidak Laku > 14 Hari)**\n\n";
                message += "| Nama Produk | Stok | Satuan |\n";
                message += "|---|---|---|\n";

                foreach (var p in deadStock.Take(15))
                {
                    message += $"| {p.Name} | {p.Stock} | {p.Unit} |\n";
                }

                message += "\n💡 *Pertimbangkan untuk promosi atau clearance sale untuk produk ini.*";

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating dead stock: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil data dead stock.";
            }
        }

        private async Task<string> HandleRestockHistoryCommandAsync(string args, string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                string productName = args.Trim();
                if (string.IsNullOrEmpty(productName))
                    return "⚠️ Format: /riwayat_restock <nama_produk>";

                // Cari produk
                var products = await _posDbService.GetAllProductsAsync();
                var product = products.FirstOrDefault(p =>
                    p.Name != null && p.Name.Contains(productName, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                    return $"❌ Produk \"{productName}\" tidak ditemukan.";

                var history = await _posDbService.GetRestockHistoryAsync(product.Id);

                if (!history.Any())
                    return $"ℹ️ **Riwayat Restock: {product.Name}**\n\nBelum ada data restock.";

                string message = $"📦 **RIWAYAT RESTOCK: {product.Name.ToUpper()}**\n\n";
                message += "| Tanggal | Qty | Harga Modal | Total |\n";
                message += "|---|---|---|---|\n";

                foreach (var h in history.Take(10))
                {
                    message += $"| {h.Date:dd/MM} | {h.Quantity} | Rp {h.UnitCost:N0} | Rp {h.TotalCost:N0} |\n";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating restock history: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil riwayat restock.";
            }
        }

        private async Task<string> HandleInventoryHistoryCommandAsync(string args, string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                string productName = args.Trim();
                if (string.IsNullOrEmpty(productName))
                    return "⚠️ Format: /riwayat_inventory <nama_produk>";

                // Cari produk
                var products = await _posDbService.GetAllProductsAsync();
                var product = products.FirstOrDefault(p =>
                    p.Name != null && p.Name.Contains(productName, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                    return $"❌ Produk \"{productName}\" tidak ditemukan.";

                var history = await _posDbService.GetInventoryHistoryAsync(product.Id);

                if (!history.Any())
                    return $"ℹ️ **Riwayat Inventory: {product.Name}**\n\nBelum ada data koreksi stok.";

                string message = $"📊 **RIWAYAT INVENTORY: {product.Name.ToUpper()}**\n\n";
                message += "| Tanggal | Stok Lama | Stok Baru | Perubahan | Alasan |\n";
                message += "|---|---|---|---|---|\n";

                foreach (var h in history.Take(10))
                {
                    string change = h.Adjustment > 0 ? $"+{h.Adjustment}" : h.Adjustment.ToString();
                    message += $"| {h.Timestamp:dd/MM} | {h.OldStock} | {h.NewStock} | {change} | {h.Reason} |\n";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating inventory history: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil riwayat inventory.";
            }
        }

        private async Task<string> HandleAutoRestockRecommendationCommandAsync(string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                var recommendations = await _posDbService.GetAutoRestockRecommendationsAsync();

                if (!recommendations.Any())
                    return "✅ **Semua stok aman!**\n\nTidak ada produk yang perlu direstock.";

                string message = "📦 **REKOMENDASI RESTOCK OTOMATIS**\n\n";
                message += "| Produk | Stok Saat Ini | Rekomendasi | Prioritas |\n";
                message += "|---|---|---|---|\n";

                foreach (var r in recommendations.Take(15))
                {
                    string priority = r.Priority == "HIGH" ? "🔴 TINGGI" : r.Priority == "MEDIUM" ? "🟡 SEDANG" : "🟢 RENDAH";
                    message += $"| {r.ProductName} | {r.CurrentStock} {r.Unit} | {r.RecommendedQty} {r.Unit} | {priority} |\n";
                }

                message += "\n💡 *Rekomendasi berdasarkan pola penjualan 30 hari terakhir.*";

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating auto restock: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil rekomendasi restock.";
            }
        }

        private async Task<string> HandleStockNotificationCommandAsync(string userId, string channel)
        {
            if (_posDbService == null)
                return "⚠️ Database pos.db belum dikonfigurasi.";

            try
            {
                var lowStock = await _posDbService.GetLowStockProductsAsync(20);
                var outOfStock = lowStock.Where(p => p.Stock <= 0).ToList();
                var criticalStock = lowStock.Where(p => p.Stock > 0 && p.Stock <= 5).ToList();

                string message = "📢 **NOTIFIKASI STOK**\n\n";

                if (outOfStock.Any())
                {
                    message += "🚨 **STOK HABIS**\n";
                    foreach (var p in outOfStock.Take(10))
                    {
                        message += $"🔴 {p.Name}: {p.Stock} {p.Unit}\n";
                    }
                    message += "\n";
                }

                if (criticalStock.Any())
                {
                    message += "⚠️ **STOK KRITIS (≤5)**\n";
                    foreach (var p in criticalStock.Take(10))
                    {
                        message += $"🟡 {p.Name}: {p.Stock} {p.Unit}\n";
                    }
                    message += "\n";
                }

                if (!outOfStock.Any() && !criticalStock.Any())
                {
                    message += "✅ **Semua stok dalam kondisi baik!**\n\nTidak ada notifikasi stok.";
                }

                return message;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error generating stock notification: {ex.Message}", "CommandHandler");
                return "⚠️ Terjadi kesalahan saat mengambil notifikasi stok.";
            }
        }
    }
}