using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class TelegramBotService
    {
        private TelegramBotClient? _botClient;
        private readonly ConfigService _configService;
        private readonly GroqService _groqService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private bool _isRunning = false;
        private CancellationTokenSource? _cancellationTokenSource;

        public bool IsRunning => _isRunning;

        public TelegramBotService(
            ConfigService configService,
            GroqService groqService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService = null)
        {
            _configService = configService;
            _groqService = groqService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;
        }

        public async Task<bool> StartAsync()
        {
            try
            {
                string? botToken = _configService.Config?.Telegram?.BotToken;
                if (string.IsNullOrEmpty(botToken) || botToken == "YOUR_TELEGRAM_BOT_TOKEN")
                {
                    await _loggingService.LogErrorAsync("Bot token belum dikonfigurasi", "Telegram");
                    return false;
                }

                _botClient = new TelegramBotClient(botToken);
                _cancellationTokenSource = new CancellationTokenSource();

                // Test koneksi
                var me = await _botClient.GetMeAsync(_cancellationTokenSource.Token);
                await _loggingService.LogInfoAsync($"Bot dimulai: @{me.Username}", "Telegram");

                // Start polling
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    pollingErrorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: _cancellationTokenSource.Token);

                _isRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogCriticalAsync(
                    $"Gagal memulai bot: {ex.Message}",
                    "Telegram",
                    ex.ToString());
                _isRunning = false;
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _isRunning = false;
                await _loggingService.LogInfoAsync("Bot dihentikan", "Telegram");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error saat menghentikan bot: {ex.Message}",
                    "Telegram",
                    ex.ToString());
            }
        }

        /// <summary>
        /// Kirim Laporan Pagi Otomatis ke Owner
        /// </summary>
        public async Task SendMorningReportAsync()
        {
            if (_botClient == null || !_isRunning) return;

            try
            {
                var config = _configService.Config;
                if (config?.Telegram?.AllowedChatIds == null || !config.Telegram.AllowedChatIds.Any())
                    return;

                // Kirim ke semua Owner (AllowedChatIds)
                foreach (var chatId in config.Telegram.AllowedChatIds)
                {
                    if (_posDbService == null) continue;

                    // Ambil Data
                    var revenue = await _posDbService.GetTodayRevenueAsync(); // Hari ini (biasanya 0 pagi2)
                    var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
                    var transactions = await _posDbService.GetRecentTransactionsAsync(1); // Cek ada transaksi gak
                    var lowStock = await _posDbService.GetLowStockProductsAsync(5);
                    var topSelling = await _posDbService.GetTopSellingProductsByDateAsync(DateTime.Now.AddDays(-1), 3);

                    string message = $"📊 **LAPORAN PAGI {DateTime.Now:dd/MM/yyyy}**\n\n";
                    message += $"💰 Omzet Kemarin: Rp {yesterdayRevenue:N0}\n";
                    message += $"🧾 Total Transaksi: {transactions.Count} nota\n\n";

                    if (lowStock.Any())
                    {
                        message += "⚠️ **Stok Minus/Rendah:**\n";
                        foreach (var p in lowStock.Take(3))
                        {
                            message += $"- {p.Name}: {p.Stock} {p.Unit}\n";
                        }
                        message += "\n";
                    }

                    if (topSelling.Any())
                    {
                        message += "🔥 **Produk Terlaris Kemarin:**\n";
                        foreach (var p in topSelling)
                        {
                            message += $"- {p.Name}\n";
                        }
                    }

                    await _botClient.SendTextMessageAsync(
                        chatId,
                        message,
                        parseMode: ParseMode.Markdown);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error sending morning report: {ex.Message}", "Scheduler");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Handle Callback Query (untuk tombol konfirmasi)
                if (update.Type == UpdateType.CallbackQuery)
                {
                    var callbackQuery = update.CallbackQuery;
                    if (callbackQuery?.Data != null && callbackQuery.Message != null)
                    {
                        await HandleCallbackQueryAsync(botClient, callbackQuery, cancellationToken);
                    }
                    return;
                }

                // Handle Message updates
                if (update.Type == UpdateType.Message)
                {
                    var message = update.Message;
                    if (message == null) return;

                    // Cek whitelist chat ID
                    if (!IsChatAllowed(message.Chat.Id))
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            "⚠️ Maaf, Anda tidak memiliki akses ke bot ini.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Handle photo (OCR)
                    if (message.Type == MessageType.Photo)
                    {
                        await HandlePhotoMessageAsync(botClient, message, cancellationToken);
                        return;
                    }

                    // Handle text message
                    if (message.Type == MessageType.Text)
                    {
                        await HandleTextMessageAsync(botClient, message, cancellationToken);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling update: {ex.Message}",
                    "Telegram",
                    ex.ToString());
            }
        }

        private async Task HandleTextMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            string text = message.Text ?? "";
            long chatId = message.Chat.Id;
            string? userName = message.From?.Username ?? message.From?.FirstName ?? "Unknown";

            // Simpan pesan user ke database
            await _databaseService.AddConversationAsync(new Conversation
            {
                ChatId = chatId,
                UserName = userName,
                Role = "user",
                Message = text,
                Timestamp = DateTime.Now,
                MessageType = "text"
            });

            // Cek apakah command
            if (text.StartsWith("/"))
            {
                await HandleCommandAsync(botClient, message, text, chatId, userName, cancellationToken);
                return;
            }

            // Natural language processing dengan AI
            try
            {
                // Tampilkan typing indicator
                await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

                // Ambil history percakapan
                var history = await _databaseService.GetRecentConversationsAsync(chatId, 5);
                var historyTexts = history.Select(c => $"{c.Role}: {c.Message}").ToList();

                // Cek Role User
                bool isOwner = IsOwner(chatId);
                string userRole = GetUserRole(chatId);

                // AMBIL DATA REAL DARI DATABASE untuk konteks AI
                string? realDataInfo = null;
                if (_posDbService != null)
                {
                    try
                    {
                        // Ambil data hari ini
                        var todayRevenue = await _posDbService.GetTodayRevenueAsync();
                        var todayProfit = await _posDbService.GetTodayProfitAsync();
                        
                        // Ambil data kemarin
                        var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
                        var yesterdayTopSelling = await _posDbService.GetTopSellingProductsByDateAsync(DateTime.Now.AddDays(-1), 5);
                        
                        // Ambil nama toko
                        var shopName = await _posDbService.GetShopNameAsync();
                        
                        // Ambil produk terlaris hari ini
                        var topSelling = await _posDbService.GetTopSellingProductsAsync(5);
                        
                        // Ambil data stok rendah
                        var lowStock = await _posDbService.GetLowStockProductsAsync(10);
                        
                        // SMART SEARCH: Cari produk yang relevan dengan pertanyaan user
                        var relevantProducts = await SearchRelevantProductsAsync(text);
                        
                        // Hitung statistik stok
                        var allProducts = await _posDbService.GetAllProductsAsync();
                        int totalProducts = allProducts.Count;
                        int safeStock = allProducts.Count(p => p.Stock > 10);
                        int lowStockCount = allProducts.Count(p => p.Stock > 0 && p.Stock <= 10);
                        int outOfStock = allProducts.Count(p => p.Stock <= 0);
                        int negativeStock = allProducts.Count(p => p.Stock < 0);
                        
                        // Ambil data pelanggan
                        var topCustomers = await _posDbService.GetTopCustomersAsync(5);
                        int totalCustomers = await _posDbService.GetTotalCustomersAsync();
                        
                        // Build konteks data real
                        realDataInfo = $"\n\n🏪 DATA REAL {shopName.ToUpper()}:\n";
                        realDataInfo += $"📅 Hari Ini ({DateTime.Now:dd/MM/yyyy}):\n";
                        realDataInfo += $"  - Revenue: Rp {todayRevenue:N0}\n";
                        
                        // Jika Owner, tampilkan Profit. Jika Kasir, sembunyikan.
                        if (isOwner)
                        {
                            realDataInfo += $"  - Profit: Rp {todayProfit:N0}\n";
                        }
                        
                        realDataInfo += $"  - Produk Terlaris: {(topSelling.Any() ? string.Join(", ", topSelling.Take(3).Select(p => p.Name)) : "Tidak ada data")}\n";
                        realDataInfo += $"📅 Kemarin ({DateTime.Now.AddDays(-1):dd/MM/yyyy}):\n";
                        realDataInfo += $"  - Revenue: Rp {yesterdayRevenue:N0}\n";
                        realDataInfo += $"  - Produk Terlaris: {(yesterdayTopSelling.Any() ? string.Join(", ", yesterdayTopSelling.Take(3).Select(p => p.Name)) : "Tidak ada data")}\n";
                        realDataInfo += $"📊 Summary Stok: {totalProducts} produk total | {safeStock} aman | {lowStockCount} rendah | {outOfStock} habis | {negativeStock} minus\n";
                        realDataInfo += $"👥 Pelanggan: {totalCustomers} total pelanggan\n";
                        
                        // Tambah data pelanggan teratas
                        if (topCustomers.Any())
                        {
                            realDataInfo += "\n🏆 PELANGGAN TERATAS (Paling Sering Belanja):\n";
                            foreach (var c in topCustomers.Take(5))
                            {
                                realDataInfo += $"- {c.Name}: {c.PurchaseCount}x belanja, Total Rp {c.TotalSpent:N0}\n";
                                if (c.LastPurchaseDate.HasValue)
                                {
                                    realDataInfo += $"  Terakhir belanja: {c.LastPurchaseDate.Value:dd/MM/yyyy}\n";
                                }
                            }

                            // DETEKSI apakah user tanya tentang pelanggan tertentu
                            // Jika ya, ambil riwayat belanja pelanggan tersebut
                            var mentionedCustomer = topCustomers.FirstOrDefault(c =>
                                !string.IsNullOrEmpty(c.Name) &&
                                text.ToLower().Contains(c.Name!.ToLower()));

                            if (mentionedCustomer != null && !string.IsNullOrEmpty(mentionedCustomer.Id))
                            {
                                // Ambil riwayat transaksi pelanggan ini
                                var customerTransactions = await _posDbService.GetCustomerTransactionsAsync(mentionedCustomer.Id, 15);

                                if (customerTransactions.Any())
                                {
                                    realDataInfo += $"\n🛒 RIWAYAT BELANJA {mentionedCustomer.Name.ToUpper()} (15 transaksi terakhir):\n";
                                    
                                    // Group by transaction date
                                    var groupedByDate = customerTransactions
                                        .GroupBy(t => t.Date?.ToString("dd/MM/yyyy") ?? "Unknown")
                                        .ToList();

                                    foreach (var dateGroup in groupedByDate.Take(5))
                                    {
                                        realDataInfo += $"\n📅 Tanggal {dateGroup.Key}:\n";
                                        foreach (var t in dateGroup.Take(5))
                                        {
                                            realDataInfo += $"  - {t.ProductName}: {t.Quantity} x Rp {t.Price:N0} = Rp {t.ItemTotal:N0}\n";
                                        }
                                    }

                                    if (customerTransactions.Count > 15)
                                    {
                                        realDataInfo += $"\n  ... dan {customerTransactions.Count - 15} transaksi lainnya";
                                    }
                                }
                            }
                        }
                        
                        // Tambah produk terlaris hari ini dengan detail harga modal & jual
                        if (topSelling.Any())
                        {
                            realDataInfo += "\n🔥 PRODUK TERLARIS HARI INI:\n";
                            foreach (var p in topSelling.Take(5))
                            {
                                decimal displayStock = p.Stock < 0 ? 0 : (p.Stock ?? 0);
                                realDataInfo += $"- {p.Name}: Terjual {displayStock} {p.Unit}\n";
                                
                                // Jika Owner, tampilkan detail harga. Jika Kasir, hanya stok & harga jual.
                                if (isOwner)
                                {
                                    realDataInfo += $"  💰 Harga Modal: Rp {p.PurchasePrice:N0} | Harga Jual: Rp {p.SellingPrice:N0} | Margin: {p.Margin:F1}%\n";
                                }
                                else
                                {
                                    realDataInfo += $"  🏷️ Harga Jual: Rp {p.SellingPrice:N0}\n";
                                }
                            }
                        }
                        
                        // Tambah produk terlaris kemarin
                        if (yesterdayTopSelling.Any())
                        {
                            realDataInfo += "\n🔥 PRODUK TERLARIS KEMARIN:\n";
                            foreach (var p in yesterdayTopSelling.Take(5))
                            {
                                decimal displayStock = p.Stock < 0 ? 0 : (p.Stock ?? 0);
                                realDataInfo += $"- {p.Name}: Terjual {displayStock} {p.Unit}, Harga Jual Rp {p.SellingPrice:N0}\n";
                            }
                        }
                        
                        // Tambah produk stok rendah dengan harga modal
                        if (lowStock.Any())
                        {
                            realDataInfo += "\n⚠️ PRODUK STOK RENDAH:\n";
                            foreach (var p in lowStock.Take(8))
                            {
                                decimal displayStock = p.Stock < 0 ? 0 : (p.Stock ?? 0);
                                realDataInfo += $"- {p.Name}: Stok {displayStock} {p.Unit}\n";
                                
                                if (isOwner)
                                {
                                    realDataInfo += $"  💰 Harga Modal: Rp {p.PurchasePrice:N0} | Harga Jual: Rp {p.SellingPrice:N0} | Margin: {p.Margin:F1}%\n";
                                }
                                else
                                {
                                    realDataInfo += $"  🏷️ Harga Jual: Rp {p.SellingPrice:N0}\n";
                                }
                            }
                        }
                        
                        // Tambah produk relevan dari search dengan harga modal & jual
                        if (relevantProducts.Any())
                        {
                            realDataInfo += $"\n🔍 PRODUK RELEVAN (hasil pencarian \"{text}\"):\n";
                            foreach (var p in relevantProducts.Take(10))
                            {
                                string status = p.Stock <= 0 ? "🔴 HABIS" : p.Stock <= 5 ? "🟡 RENDAH" : "🟢 AMAN";
                                // PENTING: Tampilkan stok asli (termasuk minus) agar AI tahu data real
                                realDataInfo += $"- {p.Name}: Stok {p.Stock} {p.Unit} ({status})\n";
                                
                                if (isOwner)
                                {
                                    realDataInfo += $"  💰 Harga Modal: Rp {p.PurchasePrice:N0} | Harga Jual: Rp {p.SellingPrice:N0} | Margin: {p.Margin:F1}%\n";
                                }
                                else
                                {
                                    realDataInfo += $"  🏷️ Harga Jual: Rp {p.SellingPrice:N0}\n";
                                }
                            }
                        }
                        else
                        {
                            // Jika tidak ada produk relevan, beri tahu AI untuk jujur
                            realDataInfo += "\n💡 CATATAN: Jika user tanya produk spesifik, cari di semua database. JANGAN mengarang!";
                        }
                    }
                    catch (Exception dbEx)
                    {
                        await _loggingService.LogWarningAsync($"Error fetching real data for AI: {dbEx.Message}", "AI");
                        realDataInfo = "\n\n(Tidak ada data database tersedia saat ini - JANGAN mengarang data!)";
                    }
                }

                // Generate response dengan data real
                string response = await _groqService.GenerateNaturalResponseAsync(
                    text, 
                    historyTexts, 
                    userRole,
                    realDataInfo);

                // Simpan response AI ke database
                await _databaseService.AddConversationAsync(new Conversation
                {
                    ChatId = chatId,
                    UserName = "SSA",
                    Role = "assistant",
                    Message = response,
                    Timestamp = DateTime.Now,
                    MessageType = "text"
                });

                // Kirim response
                await botClient.SendTextMessageAsync(
                    chatId,
                    response,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating AI response: {ex.Message}",
                    "AI",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Maaf, terjadi kesalahan saat memproses permintaan Anda. Silakan coba lagi.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleCommandAsync(
            ITelegramBotClient botClient,
            Message message,
            string command,
            long chatId,
            string userName,
            CancellationToken cancellationToken)
        {
            try
            {
                string[] parts = command.Split(' ', 2);
                string cmd = parts[0].ToLower();
                string args = parts.Length > 1 ? parts[1] : "";

                // Cek Role User
                bool isOwner = IsOwner(chatId);

                switch (cmd)
                {
                    case "/start":
                    case "/help":
                        await SendHelpMessageAsync(botClient, chatId, isOwner, cancellationToken);
                        break;

                    case "/stok":
                        await HandleStockCommandAsync(botClient, chatId, args, cancellationToken);
                        break;

                    case "/laporan":
                        await HandleLaporanCommandAsync(botClient, chatId, isOwner, cancellationToken);
                        break;

                    case "/restock":
                        await HandleRestockCommandAsync(botClient, message, args, cancellationToken);
                        break;

                    case "/inventory":
                    case "/quick_inventory":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Quick Inventory hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleInventoryCommandAsync(botClient, message, args, cancellationToken);
                        break;

                    case "/analisa":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, " Akses Ditolak: Fitur Analisa hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleAnalisaCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    case "/cek_modal":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Cek Modal hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleCekModalCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    case "/laporan_kasir":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Laporan Kasir hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleLaporanKasirCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    case "/dead_stock":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Dead Stock hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleDeadStockCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    case "/riwayat_restock":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Riwayat Restock hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleRestockHistoryCommandAsync(botClient, chatId, args, cancellationToken);
                        break;

                    case "/riwayat_inventory":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Riwayat Inventory hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleInventoryHistoryCommandAsync(botClient, chatId, args, cancellationToken);
                        break;

                    case "/rekomendasi_restock":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Rekomendasi Restock hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleAutoRestockRecommendationCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    case "/notifikasi_stok":
                        if (!isOwner)
                        {
                            await botClient.SendTextMessageAsync(chatId, "⛔ Akses Ditolak: Fitur Notifikasi Stok hanya untuk Owner.", cancellationToken: cancellationToken);
                            return;
                        }
                        await HandleStockNotificationCommandAsync(botClient, chatId, cancellationToken);
                        break;

                    default:
                        await botClient.SendTextMessageAsync(
                            chatId,
                            $"⚠️ Command tidak dikenal: {cmd}\n\nKetik /help untuk melihat daftar command yang tersedia.",
                            cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling command {command}: {ex.Message}",
                    "Command",
                    ex.ToString(),
                    userName);

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat memproses command. Silakan coba lagi.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task SendHelpMessageAsync(ITelegramBotClient botClient, long chatId, bool isOwner, CancellationToken cancellationToken)
        {
            string helpMessage = @"🏪 **Smart Sembako Assistant**

**Command Umum:**
`/stok [nama]` - Cek stok produk
`/laporan` - Laporan hari ini
`/restock [nama]` - Rekomendasi restock
`/help` - Bantuan

**Command Owner:**
";
            if (isOwner)
            {
                helpMessage += @"`/analisa` - Analisa bisnis lengkap
`/cek_modal` - Cek produk tanpa modal
`/laporan_kasir` - Performa kasir
`/dead_stock` - Barang tidak laku > 14 hari
`/inventory <produk> <qty>` - Koreksi stok (Quick Inventory)
`/riwayat_restock <produk>` - Riwayat restock produk
`/riwayat_inventory <produk>` - Riwayat inventory produk
`/rekomendasi_restock` - Rekomendasi restock otomatis
`/notifikasi_stok` - Cek produk stok habis/minus
";
            }
            else
            {
                helpMessage += @"*(Tidak ada command khusus)*
";
            }

            helpMessage += @"
**Chat Natural:**
Anda bisa chat biasa, contoh:
- ""Stok beras berapa?""
- ""Gimana penjualan hari ini?""";

            await botClient.SendTextMessageAsync(
                chatId,
                helpMessage,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }

        private async Task HandleStockCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            string searchQuery,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

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

                    await botClient.SendTextMessageAsync(
                        chatId,
                        message,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"❌ Produk dengan nama \"{searchQuery}\" tidak ditemukan.",
                        cancellationToken: cancellationToken);
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

                    await botClient.SendTextMessageAsync(
                        chatId,
                        message,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "✅ Semua stok aman!",
                        cancellationToken: cancellationToken);
                }
            }
        }

        private async Task HandleLaporanCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            bool isOwner,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

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

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating laporan: {ex.Message}",
                    "Command");

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil laporan.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleCekModalCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                var zeroCostProducts = await _posDbService.GetZeroCostProductsAsync();

                if (!zeroCostProducts.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "✅ **Semua produk sudah memiliki harga modal!**\n\nTidak ada produk dengan modal Rp 0.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = "⚠️ **PRODUK TANPA MODAL (Cost = 0)**\n\n";
                message += "| Nama Produk | Stok | Harga Jual |\n";
                message += "|---|---|---|\n";

                foreach (var p in zeroCostProducts.Take(15))
                {
                    message += $"| {p.Name} | {p.Stock} {p.Unit} | Rp {p.SellingPrice:N0} |\n";
                }

                message += "\n💡 *Silakan update harga modal di aplikasi Aronium agar analisa profit akurat.*";

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating cek modal: {ex.Message}",
                    "Command");

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil data modal.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleLaporanKasirCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                var salesPerUser = await _posDbService.GetSalesPerUserAsync();

                if (!salesPerUser.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "ℹ️ **Belum ada data transaksi hari ini.**",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = "🧑‍ **LAPORAN PENJUALAN PER KASIR**\n\n";
                message += "| Kasir | Transaksi | Total Penjualan |\n";
                message += "|---|---|---|\n";

                foreach (var s in salesPerUser)
                {
                    message += $"| {s.Name} | {s.TransactionCount}x | Rp {s.TotalSales:N0} |\n";
                }

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating laporan kasir: {ex.Message}",
                    "Command");

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil data kasir.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleDeadStockCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                var deadStock = await _posDbService.GetDeadStockProductsAsync();

                if (!deadStock.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "✅ **Tidak ada Dead Stock!**\n\nSemua produk dengan stok terjual dalam 14 hari terakhir.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = "💀 **DEAD STOCK (Tidak Laku > 14 Hari)**\n\n";
                message += "| Nama Produk | Stok | Satuan |\n";
                message += "|---|---|---|\n";

                foreach (var p in deadStock.Take(15))
                {
                    message += $"| {p.Name} | {p.Stock} | {p.Unit} |\n";
                }

                message += "\n💡 *Pertimbangkan untuk promosi atau clearance sale untuk produk ini.*";

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating dead stock: {ex.Message}",
                    "Command");

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil data dead stock.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleRestockCommandAsync(
            ITelegramBotClient botClient,
            Message message,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                // Cek apakah ini bulk restock (format: produk1 qty1 harga1, produk2 qty2 harga2)
                if (args.Contains(','))
                {
                    await HandleBulkRestockCommandAsync(botClient, message, args, cancellationToken);
                    return;
                }

                // Format: /restock <produk> <qty> [harga_modal]
                // Contoh: /restock minyak 50 14000
                //         /restock kapal api 50 16000
                var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "📦 **Format Restock:**\n\n`/restock <nama_produk> <qty> [harga_modal]`\n\nContoh:\n`/restock minyak 50 14000`\n`/restock gula 25`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Parse dari belakang:
                // - Jika ada 2 angka di akhir: yang pertama = qty, yang kedua = harga
                // - Jika ada 1 angka di akhir: itu = qty
                decimal price = 0;
                int qtyStartIndex = parts.Length - 1;
                bool hasPrice = false;

                if (parts.Length >= 3)
                {
                    bool lastIsNumber = decimal.TryParse(parts[parts.Length - 1], out decimal lastNum);
                    bool secondLastIsNumber = decimal.TryParse(parts[parts.Length - 2], out decimal secondLastNum);

                    if (lastIsNumber && secondLastIsNumber)
                    {
                        // Dua angka di akhir: qty dan harga
                        price = lastNum;
                        qtyStartIndex = parts.Length - 2;
                        hasPrice = true;
                    }
                    else if (lastIsNumber)
                    {
                        // Satu angka di akhir: qty saja
                        qtyStartIndex = parts.Length - 1;
                    }
                }
                else if (parts.Length == 2)
                {
                    // Hanya 2 bagian: produk dan qty
                    if (decimal.TryParse(parts[1], out _))
                    {
                        qtyStartIndex = 1;
                    }
                }

                // Parse quantity
                if (!int.TryParse(parts[qtyStartIndex], out int qty) || qty <= 0)
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Quantity harus angka positif.\n\nContoh: `/restock minyak 50`",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Nama produk adalah semua bagian sebelum quantity
                string productName = string.Join(" ", parts.Take(qtyStartIndex));

                if (string.IsNullOrWhiteSpace(productName))
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Nama produk tidak boleh kosong.\n\nContoh: `/restock kapal api 50 16000`",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Cari produk
                var allProducts = await _posDbService.GetAllProductsAsync();
                var product = allProducts.FirstOrDefault(p =>
                    p.Name != null &&
                    (p.Name.ToLower().Contains(productName.ToLower()) ||
                     p.Name.ToLower().Replace(" ", "").Contains(productName.ToLower().Replace(" ", ""))));

                if (product == null)
                {
                    // Fuzzy search
                    product = allProducts.FirstOrDefault(p =>
                        p.Name != null &&
                        p.Name.ToLower().Contains(productName.Substring(0, Math.Min(3, productName.Length))));

                    if (product == null)
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Produk \"{productName}\" tidak ditemukan.\n\nGunakan `/stok {productName}` untuk mencari.",
                            cancellationToken: cancellationToken);
                        return;
                    }
                }

                // Jika harga tidak diset, gunakan harga terakhir atau 0
                if (price == 0)
                {
                    price = product.PurchasePrice ?? 0;
                }

                decimal total = qty * price;

                // Konfirmasi
                string confirmMsg = $"📦 **KONFIRMASI RESTOCK**\n\n";
                confirmMsg += $"📋 Detail:\n";
                confirmMsg += $"• Produk: {product.Name}\n";
                confirmMsg += $"• Quantity: {qty} {product.Unit}\n";
                confirmMsg += $"• Harga Modal: Rp {price:N0}/pcs\n";
                confirmMsg += $"• Total Modal: Rp {total:N0}\n\n";
                confirmMsg += $"⚠️ Aksi ini akan membuat dokumen pembelian di sistem.\n\nLanjutkan?";

                var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("✅ YA", $"restock_confirm_{product.Id}_{qty}_{price}"),
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("❌ BATAL", "restock_cancel")
                    }
                });

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    confirmMsg,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling restock command: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Terjadi kesalahan saat memproses restock.",
                    cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Handler untuk bulk restock (multiple produk sekaligus)
        /// Format: produk1 qty1 harga1, produk2 qty2 harga2, ...
        /// </summary>
        private async Task HandleBulkRestockCommandAsync(
            ITelegramBotClient botClient,
            Message message,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                // Split by comma untuk dapat setiap item
                var items = args.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                if (items.Count > 10)
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Maksimal 10 produk per bulk restock.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var allProducts = await _posDbService.GetAllProductsAsync();
                var bulkItems = new List<(Product Product, int Qty, decimal Price)>();

                foreach (var item in items)
                {
                    var parts = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Format salah untuk: \"{item}\". Gunakan: produk qty [harga]",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Parse qty dan harga
                    int qty = 0;
                    decimal price = 0;
                    int productNameEndIndex = parts.Length - 1;

                    if (parts.Length >= 3)
                    {
                        bool lastIsNumber = decimal.TryParse(parts[parts.Length - 1], out decimal lastNum);
                        bool secondLastIsNumber = decimal.TryParse(parts[parts.Length - 2], out decimal secondLastNum);

                        if (lastIsNumber && secondLastIsNumber)
                        {
                            price = lastNum;
                            qty = (int)secondLastNum;
                            productNameEndIndex = parts.Length - 2;
                        }
                        else if (lastIsNumber)
                        {
                            qty = (int)lastNum;
                            productNameEndIndex = parts.Length - 1;
                        }
                    }
                    else if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[1], out qty))
                        {
                            productNameEndIndex = 1;
                        }
                    }

                    if (qty <= 0)
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Quantity harus positif untuk: \"{item}\"",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Cari produk
                    string productName = string.Join(" ", parts.Take(productNameEndIndex));
                    var product = allProducts.FirstOrDefault(p =>
                        p.Name != null &&
                        (p.Name.ToLower().Contains(productName.ToLower()) ||
                         p.Name.ToLower().Replace(" ", "").Contains(productName.ToLower().Replace(" ", ""))));

                    if (product == null || string.IsNullOrEmpty(product.Id))
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Produk \"{productName}\" tidak ditemukan.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    bulkItems.Add((product, qty, price));
                }

                // Tampilkan konfirmasi bulk
                string confirmMsg = "📦 **KONFIRMASI BULK RESTOCK**\n\n";
                confirmMsg += $"Jumlah produk: {bulkItems.Count}\n\n";

                decimal grandTotal = 0;
                foreach (var item in bulkItems)
                {
                    decimal total = item.Qty * item.Price;
                    grandTotal += total;
                    confirmMsg += $"• {item.Product.Name}: {item.Qty} {item.Product.Unit} × Rp {item.Price:N0} = Rp {total:N0}\n";
                }

                confirmMsg += $"\n**Total Modal: Rp {grandTotal:N0}**\n\n";
                confirmMsg += "⚠️ Aksi ini akan membuat dokumen pembelian di sistem.\n\nLanjutkan?";

                // Encode data untuk callback (produk1:qty1:price1,produk2:qty2:price2)
                string callbackData = string.Join(",", bulkItems.Select(i => $"{i.Product.Id}:{i.Qty}:{i.Price}"));
                if (callbackData.Length > 64)
                {
                    // Jika terlalu panjang untuk callback, langsung eksekusi
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "⚠️ Data terlalu panjang untuk konfirmasi. Mengeksekusi langsung...",
                        cancellationToken: cancellationToken);

                    int successCount = 0;
                    foreach (var item in bulkItems)
                    {
                        var result = await _posDbService.CreatePurchaseDocumentAsync(int.Parse(item.Product.Id), item.Qty, item.Price);
                        if (result.Success) successCount++;
                    }

                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        $"✅ Bulk restock selesai: {successCount}/{bulkItems.Count} produk berhasil.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("✅ YA", $"bulk_restock_confirm_{callbackData}"),
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("❌ BATAL", "restock_cancel")
                    }
                });

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    confirmMsg,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling bulk restock: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Terjadi kesalahan saat memproses bulk restock.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleInventoryCommandAsync(
            ITelegramBotClient botClient,
            Message message,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                // Cek apakah ini bulk inventory (format CSV)
                if (args.Contains(','))
                {
                    await HandleBulkInventoryCommandAsync(botClient, message, args, cancellationToken);
                    return;
                }

                // Format: /inventory <produk> <qty_positif_atau_negatif>
                // Contoh: /inventory kapal api -10 (kurangi 10 stok)
                //         /inventory kapal api 5 (tambah 5 stok)
                var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "📦 **Format Quick Inventory:**\n\n`/inventory <nama_produk> <qty>`\n\nContoh:\n`/inventory minyak -10` (kurangi stok)\n`/inventory minyak 5` (tambah stok)",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Parse dari belakang: bagian terakhir adalah qty (bisa negatif)
                // Sisanya adalah nama produk
                int qtyStartIndex = parts.Length - 1;

                // Parse quantity (bisa negatif untuk mengurangi stok, 0 untuk reset)
                if (!int.TryParse(parts[qtyStartIndex], out int qty))
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Quantity harus angka (bisa negatif untuk mengurangi stok).\n\nContoh: `/inventory minyak -10`",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Nama produk adalah semua bagian SEBELUM quantity
                string productName = string.Join(" ", parts.Take(qtyStartIndex));

                if (string.IsNullOrWhiteSpace(productName))
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Nama produk tidak boleh kosong.\n\nContoh: `/inventory kapal api 10`",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Cari produk - gunakan exact match atau close match
                var allProducts = await _posDbService.GetAllProductsAsync();
                
                // Prioritas 1: Exact match (case-insensitive)
                var product = allProducts.FirstOrDefault(p =>
                    p.Name != null && p.Name.ToLower().Trim() == productName.ToLower().Trim());
                
                // Prioritas 2: Contains match (produk mengandung kata kunci)
                if (product == null)
                {
                    var keywords = productName.ToLower().Split(' ').Where(k => k.Length > 2).ToList();
                    product = allProducts.FirstOrDefault(p =>
                        p.Name != null && keywords.All(k => p.Name.ToLower().Contains(k)));
                }
                
                // Prioritas 3: Starts with match
                if (product == null)
                {
                    product = allProducts.FirstOrDefault(p =>
                        p.Name != null && p.Name.ToLower().StartsWith(productName.ToLower()));
                }

                if (product == null || string.IsNullOrEmpty(product.Id))
                {
                    // Tampilkan saran produk yang mirip
                    var suggestions = allProducts
                        .Where(p => p.Name != null && productName.ToLower().Split(' ')
                            .Any(k => k.Length > 2 && p.Name.ToLower().Contains(k)))
                        .Take(3)
                        .Select(p => $"• {p.Name}")
                        .ToList();

                    string errorMsg = $"❌ Produk \"{productName}\" tidak ditemukan.";
                    if (suggestions.Any())
                    {
                        errorMsg += $"\n\nMungkin yang Anda maksud:\n{string.Join("\n", suggestions)}";
                    }
                    errorMsg += "\n\nGunakan `/stok {productName}` untuk mencari.";

                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        errorMsg,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Tampilkan stok saat ini
                decimal currentStock = product.Stock ?? 0;
                int targetStock = qty; // qty sekarang adalah TARGET stok akhir

                // LOGIC INVENTORY COUNT: SET stok ke target (bukan ADD)
                // selisih = target - currentStock
                // Jika target = 0, berarti RESET stok ke 0
                int selisih = targetStock - (int)currentStock;
                decimal newStock = targetStock; // Stok akhir = target

                // Tentukan action berdasarkan selisih
                string action = selisih > 0 ? "📈 TAMBAH STOK" : selisih < 0 ? "📉 KURANGI STOK" : "🔄 SET STOK";
                
                string confirmMsg = $"📦 **KONFIRMASI {action}**\n\n";
                confirmMsg += $"📋 Detail:\n";
                confirmMsg += $"• Produk: {product.Name}\n";
                confirmMsg += $"• Stok Saat Ini: {currentStock} {product.Unit}\n";
                confirmMsg += $"• Stok Target: {targetStock} {product.Unit}\n";
                confirmMsg += $"• Selisih: {(selisih > 0 ? "+" : "")}{selisih} {product.Unit}\n";
                confirmMsg += $"• Stok Baru: {newStock} {product.Unit}\n\n";
                confirmMsg += $"⚠️ Aksi ini akan membuat dokumen Inventory Count di sistem.\n\nLanjutkan?";

                var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("✅ YA", $"inventory_confirm_{product.Id}_{targetStock}"),
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("❌ BATAL", "inventory_cancel")
                    }
                });

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    confirmMsg,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling inventory command: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Terjadi kesalahan saat memproses inventory.",
                    cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Handler untuk bulk inventory (multiple produk sekaligus)
        /// Format: produk1 qty1, produk2 qty2, ...
        /// </summary>
        private async Task HandleBulkInventoryCommandAsync(
            ITelegramBotClient botClient,
            Message message,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                // Split by comma untuk dapat setiap item
                var items = args.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                if (items.Count > 10)
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "❌ Maksimal 10 produk per bulk inventory.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var allProducts = await _posDbService.GetAllProductsAsync();
                var bulkItems = new List<(Product Product, int Qty)>();

                foreach (var item in items)
                {
                    var parts = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Format salah untuk: \"{item}\". Gunakan: produk qty",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Parse qty dari bagian terakhir
                    int qty = 0;
                    int productNameEndIndex = parts.Length - 1;

                    // Coba parse dari belakang, ambil angka pertama yang valid
                    bool foundQty = false;
                    for (int i = parts.Length - 1; i >= 0; i--)
                    {
                        if (int.TryParse(parts[i], out qty))
                        {
                            productNameEndIndex = i;
                            foundQty = true;
                            break;
                        }
                    }

                    if (!foundQty)
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Quantity harus angka untuk: \"{item}\"",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Cari produk
                    string productName = string.Join(" ", parts.Take(productNameEndIndex));
                    
                    // Prioritas 1: Exact match
                    var product = allProducts.FirstOrDefault(p =>
                        p.Name != null && p.Name.ToLower().Trim() == productName.ToLower().Trim());
                    
                    // Prioritas 2: Contains all keywords
                    if (product == null)
                    {
                        var keywords = productName.ToLower().Split(' ').Where(k => k.Length > 2).ToList();
                        product = allProducts.FirstOrDefault(p =>
                            p.Name != null && keywords.All(k => p.Name.ToLower().Contains(k)));
                    }
                    
                    // Prioritas 3: Starts with
                    if (product == null)
                    {
                        product = allProducts.FirstOrDefault(p =>
                            p.Name != null && p.Name.ToLower().StartsWith(productName.ToLower()));
                    }

                    if (product == null || string.IsNullOrEmpty(product.Id))
                    {
                        await botClient.SendTextMessageAsync(
                            message.Chat.Id,
                            $"❌ Produk \"{productName}\" tidak ditemukan.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    bulkItems.Add((product, qty));
                }

                // Tampilkan konfirmasi bulk
                string confirmMsg = "📦 **KONFIRMASI BULK INVENTORY**\n\n";
                confirmMsg += $"Jumlah produk: {bulkItems.Count}\n\n";

                foreach (var item in bulkItems)
                {
                    decimal currentStock = item.Product.Stock ?? 0;
                    int targetStock = item.Qty; // qty adalah TARGET
                    
                    // LOGIC INVENTORY COUNT: SET stok ke target
                    int selisih = targetStock - (int)currentStock;
                    decimal newStock = targetStock;
                    string action = selisih > 0 ? "📈" : selisih < 0 ? "📉" : "🔄";
                    
                    string perubahanText = selisih == 0 
                        ? $"{currentStock} → {newStock} (No change)" 
                        : $"{currentStock} → {newStock} {item.Product.Unit} ({(selisih > 0 ? "+" : "")}{selisih})";
                    
                    confirmMsg += $"{action} **{item.Product.Name}**: {perubahanText}\n";
                }

                confirmMsg += "\n⚠️ Aksi ini akan membuat dokumen Inventory Count di sistem.\n\nLanjutkan?";

                // Encode data untuk callback (produk1:qty1,produk2:qty2,...)
                string callbackData = string.Join(",", bulkItems.Select(i => $"{i.Product.Id}:{i.Qty}"));
                if (callbackData.Length > 64)
                {
                    // Jika terlalu panjang untuk callback, langsung eksekusi
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "⚠️ Data terlalu panjang untuk konfirmasi. Mengeksekusi langsung...",
                        cancellationToken: cancellationToken);

                    int successCount = 0;
                    foreach (var item in bulkItems)
                    {
                        var result = await _posDbService.CreateInventoryCountDocumentAsync(int.Parse(item.Product.Id), item.Qty);
                        if (result.Success) successCount++;
                    }

                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        $"✅ Bulk inventory selesai: {successCount}/{bulkItems.Count} produk berhasil.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("✅ YA", $"bulk_inventory_confirm_{callbackData}"),
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("❌ BATAL", "inventory_cancel")
                    }
                });

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    confirmMsg,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling bulk inventory: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ Terjadi kesalahan saat memproses bulk inventory.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            try
            {
                string data = callbackQuery.Data;
                long chatId = callbackQuery.Message!.Chat.Id;
                int messageId = callbackQuery.Message.MessageId;

                if (data.StartsWith("restock_confirm_"))
                {
                    // Format: restock_confirm_{productId}_{qty}_{price}
                    var parts = data.Substring("restock_confirm_".Length).Split('_');
                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out int productId) &&
                        int.TryParse(parts[1], out int qty) &&
                        decimal.TryParse(parts[2], out decimal price))
                    {
                        // Execute Restock
                        var result = await _posDbService.CreatePurchaseDocumentAsync(productId, qty, price);

                        if (result.Success)
                        {
                            string successMsg = $"✅ **RESTOCK BERHASIL**\n\n";
                            successMsg += $"📦 Detail:\n";
                            successMsg += $"• Dokumen: {result.DocumentNumber}\n";
                            successMsg += $"• Total Modal: Rp {result.Total:N0}\n\n";
                            successMsg += $"Stok akan otomatis bertambah setelah dokumen diproses Aronium.";

                            await botClient.EditMessageTextAsync(
                                chatId,
                                messageId,
                                successMsg,
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.EditMessageTextAsync(
                                chatId,
                                messageId,
                                $"❌ **RESTOCK GAGAL**\n\n{result.Error}",
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Data tidak valid", cancellationToken: cancellationToken);
                    }
                }
                else if (data.StartsWith("bulk_restock_confirm_"))
                {
                    // Format: bulk_restock_confirm_{productId1:qty1:price1,productId2:qty2:price2,...}
                    string bulkData = data.Substring("bulk_restock_confirm_".Length);
                    var items = bulkData.Split(',');

                    int successCount = 0;
                    int failCount = 0;
                    string lastError = "";

                    foreach (var item in items)
                    {
                        var parts = item.Split(':');
                        if (parts.Length == 3 &&
                            int.TryParse(parts[0], out int productId) &&
                            int.TryParse(parts[1], out int qty) &&
                            decimal.TryParse(parts[2], out decimal price))
                        {
                            var result = await _posDbService.CreatePurchaseDocumentAsync(productId, qty, price);
                            if (result.Success) successCount++;
                            else { failCount++; lastError = result.Error; }
                        }
                    }

                    string resultMsg = $"✅ **BULK RESTOCK SELESAI**\n\n";
                    resultMsg += $"Berhasil: {successCount} produk\n";
                    if (failCount > 0) resultMsg += $"Gagal: {failCount} produk\n";
                    if (!string.IsNullOrEmpty(lastError)) resultMsg += $"\nError: {lastError}";

                    await botClient.EditMessageTextAsync(
                        chatId,
                        messageId,
                        resultMsg,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else if (data == "restock_cancel")
                {
                    await botClient.EditMessageTextAsync(
                        chatId,
                        messageId,
                        "❌ Restock dibatalkan.",
                        cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("inventory_confirm_"))
                {
                    // Format: inventory_confirm_{productId}_{targetStock}
                    var parts = data.Substring("inventory_confirm_".Length).Split('_');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int productId) &&
                        int.TryParse(parts[1], out int targetStock))
                    {
                        // Execute Inventory Count dengan logic SET
                        // selisih = target - currentStock
                        var result = await _posDbService.CreateInventoryCountDocumentAsync(productId, targetStock);

                        if (result.Success)
                        {
                            // Hitung selisih untuk message
                            int selisih = (int)result.NewStock; // CreateInventoryCountDocumentAsync sudah menghitung newStock
                            string action = selisih > 0 ? "📈 STOK DITAMBAH" : selisih < 0 ? "📉 STOK DIKURANGI" : "🔄 SET STOK";
                            
                            string successMsg = $"✅ **INVENTORY BERHASIL - {action}**\n\n";
                            successMsg += $"📦 Detail:\n";
                            successMsg += $"• Dokumen: {result.DocumentNumber}\n";
                            successMsg += $"• Stok Akhir: {result.NewStock} Pcs\n\n";
                            successMsg += $"Stok telah dikoreksi di sistem.";

                            await botClient.EditMessageTextAsync(
                                chatId,
                                messageId,
                                successMsg,
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.EditMessageTextAsync(
                                chatId,
                                messageId,
                                $"❌ **INVENTORY GAGAL**\n\n{result.Error}",
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Data tidak valid", cancellationToken: cancellationToken);
                    }
                }
                else if (data.StartsWith("bulk_inventory_confirm_"))
                {
                    // Format: bulk_inventory_confirm_{productId1:qty1,productId2:qty2,...}
                    string bulkData = data.Substring("bulk_inventory_confirm_".Length);
                    var items = bulkData.Split(',');

                    int successCount = 0;
                    int failCount = 0;
                    string lastError = "";

                    foreach (var item in items)
                    {
                        var parts = item.Split(':');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int productId) &&
                            int.TryParse(parts[1], out int qty))
                        {
                            var result = await _posDbService.CreateInventoryCountDocumentAsync(productId, qty);
                            if (result.Success) successCount++;
                            else { failCount++; lastError = result.Error; }
                        }
                    }

                    string resultMsg = $"✅ **BULK INVENTORY SELESAI**\n\n";
                    resultMsg += $"Berhasil: {successCount} produk\n";
                    if (failCount > 0) resultMsg += $"Gagal: {failCount} produk\n";
                    if (!string.IsNullOrEmpty(lastError)) resultMsg += $"\nError: {lastError}";

                    await botClient.EditMessageTextAsync(
                        chatId,
                        messageId,
                        resultMsg,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else if (data == "inventory_cancel")
                {
                    await botClient.EditMessageTextAsync(
                        chatId,
                        messageId,
                        "❌ Inventory dibatalkan.",
                        cancellationToken: cancellationToken);
                }

                // Acknowledge callback
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling callback: {ex.Message}",
                    "Command",
                    ex.ToString());
            }
        }

        private async Task HandleAnalisaCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

                var products = await _posDbService.GetAllProductsAsync();
                var revenue = await _posDbService.GetTodayRevenueAsync();
                var profit = await _posDbService.GetTodayProfitAsync();
                var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();

                // Hitung margin rata-rata dari produk yang punya data cost > 0 dan masuk akal (< 200%)
                var productsWithValidMargin = products
                    .Where(p => p.PurchasePrice > 0 && p.SellingPrice > 0 && p.Margin.HasValue && p.Margin >= 0 && p.Margin <= 200)
                    .ToList();

                decimal avgMargin = productsWithValidMargin.Any()
                    ? productsWithValidMargin.Average(p => p.Margin!.Value)
                    : 0;

                // Top products by margin (filter yang masuk akal 0-100%)
                var topProducts = products
                    .Where(p => p.PurchasePrice > 0 && p.SellingPrice > 0 && p.Margin.HasValue && p.Margin >= 0 && p.Margin <= 100)
                    .OrderByDescending(p => p.Margin)
                    .Take(5)
                    .ToList();

                // Produk dengan stok rendah
                var lowStockCount = products.Count(p => p.Stock > 0 && p.Stock <= 10);
                var outOfStockCount = products.Count(p => p.Stock <= 0);
                var negativeStockCount = products.Count(p => p.Stock < 0);

                string message = $@"📈 **Analisa Bisnis {DateTime.Now:dd/MM/yyyy}**

**Keuangan:**
- Revenue Hari Ini: Rp {revenue:N0}
- Revenue Kemarin: Rp {yesterdayRevenue:N0}
- Profit Hari Ini: Rp {profit:N0}
- Margin Rata-rata Produk: {avgMargin:F1}%

**Stok:**
- Total Produk: {products.Count}
- Stok Aman: {products.Count(p => p.Stock > 10)}
- Stok Rendah (1-10): {lowStockCount}
- Habis: {outOfStockCount}
- Minus: {negativeStockCount}

**Top 5 Produk Margin Tertinggi:**
{(topProducts.Any() ? string.Join("\n", topProducts.Select(p => $"💎 {p.Name}: Margin {p.Margin:F1}%")) : "- Tidak ada data margin yang valid")}

💡 *Tips: Update harga modal di Aronium agar analisa profit lebih akurat.*";

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error generating analisa: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat membuat analisa.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandlePhotoMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            // TODO: Implement OCR processing
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                "📸 Foto diterima! Fitur OCR akan segera tersedia di update berikutnya.",
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// SMART SEARCH: Cari produk yang relevan dengan pertanyaan user
        /// Extract keywords dari pertanyaan dan search di database
        /// </summary>
        private async Task<List<Product>> SearchRelevantProductsAsync(string userMessage)
        {
            if (_posDbService == null)
                return new List<Product>();

            try
            {
                // Extract keywords dari pertanyaan user
                // Contoh: "Stok asin berapa?" → keyword: "asin"
                // "Roti apa yang paling laku?" → keyword: "roti"
                var keywords = ExtractKeywords(userMessage);
                
                if (!keywords.Any())
                    return new List<Product>();

                // Ambil semua produk dari database
                var allProducts = await _posDbService.GetAllProductsAsync();
                
                // Search produk yang match dengan keywords
                var relevantProducts = allProducts
                    .Where(p => p.Name != null && 
                        keywords.Any(keyword => 
                            p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(p => p.Name)
                    .ToList();

                return relevantProducts;
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Error searching products: {ex.Message}", "AI");
                return new List<Product>();
            }
        }

        /// <summary>
        /// Extract keywords dari pertanyaan user untuk product search
        /// Menghilangkan kata-kata umum dan menyisakan kata kunci produk
        /// </summary>
        private List<string> ExtractKeywords(string message)
        {
            // Kata-kata umum yang harus diabaikan (stopwords Bahasa Indonesia)
            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "yang", "dan", "atau", "dengan", "untuk", "dari", "ini", "itu",
                "ada", "apa", "berapa", "bagaimana", "kapan", "dimana", "siapa",
                "kenapa", "mengapa", "cara", "tentang", "pada", "di", "ke", "se",
                "tidak", "bukan", "belum", "sudah", "akan", "sedang", "telah",
                "stok", "produk", "jual", "beli", "harga", "total", "hari",
                "penjualan", "revenue", "profit", "uang", "laporan", "data",
                "informasi", "tolong", "minta", "kasih", "beri", "tampilkan",
                "cari", "cek", "lihat", "tahu", "mau", "ingin", "butuh",
                "berapa", "berapa", "ada", "ada", "apa", "itu", "ini", "nih", "dong",
                "sih", "deh", "ya", "kok", "loh", "nah", "wkwk", "haha", "hehe",
                "oke", "ok", "good", "nice", "mantap", "keren", "top", "makasih",
                "terima", "kasih", "thanks", "thank", "you", "saja", "cuma", "hanya",
                "aja", "banget", "si", "para", "para", "semua", "semua", "lain",
                "lagi", "punya", "milik", "milik", "nama", "merk", "merek", "jenis",
                "macam", "macam", "macam"
            };

            // Bersihkan message dan split menjadi kata-kata
            var words = message.ToLower()
                .Replace("?", "")
                .Replace("!", "")
                .Replace(",", "")
                .Replace(".", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2) // Minimal 3 karakter
                .Where(w => !stopwords.Contains(w))
                .Distinct()
                .ToList();

            return words;
        }

        private async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            await _loggingService.LogErrorAsync(
                $"Polling error: {exception.Message}",
                "Telegram",
                exception.ToString());
        }

        private bool IsChatAllowed(long chatId)
        {
            var allowedChatIds = _configService.Config?.Telegram?.AllowedChatIds;

            // Jika tidak ada whitelist, semua diizinkan (untuk development)
            if (allowedChatIds == null || !allowedChatIds.Any())
                return true;

            return allowedChatIds.Contains(chatId);
        }

        /// <summary>
        /// Cek apakah user adalah Owner berdasarkan Chat ID
        /// </summary>
        private bool IsOwner(long chatId)
        {
            var config = _configService.Config?.Telegram;
            
            // Prioritas 1: Cek OwnerChatIds jika ada
            if (config?.OwnerChatIds != null && config.OwnerChatIds.Any())
            {
                return config.OwnerChatIds.Contains(chatId);
            }
            
            // Prioritas 2: Fallback ke AllowedChatIds (backward compatibility)
            if (config?.AllowedChatIds != null && config.AllowedChatIds.Any())
            {
                return config.AllowedChatIds.Contains(chatId);
            }
            
            // Default: Jika tidak ada konfigurasi, anggap semua Owner (mode development)
            return true;
        }

        /// <summary>
        /// Cek apakah user adalah Kasir berdasarkan Chat ID
        /// </summary>
        private bool IsKasir(long chatId)
        {
            var config = _configService.Config?.Telegram;
            
            // Cek KasirChatIds jika ada
            if (config?.KasirChatIds != null && config.KasirChatIds.Any())
            {
                return config.KasirChatIds.Contains(chatId);
            }
            
            // Jika tidak ada KasirChatIds tapi ada AllowedChatIds, anggap yang tidak ada di AllowedChatIds adalah Kasir
            if (config?.AllowedChatIds != null && config.AllowedChatIds.Any())
            {
                return !config.AllowedChatIds.Contains(chatId);
            }
            
            return false;
        }

        /// <summary>
        /// Cek role user: "Owner" atau "Kasir"
        /// </summary>
        private string GetUserRole(long chatId)
        {
            if (IsOwner(chatId)) return "Owner";
            if (IsKasir(chatId)) return "Kasir";
            return "Owner"; // Default
        }

        #region Phase 2 & 3 Command Handlers

        /// <summary>
        /// Handler untuk /riwayat_restock [produk]
        /// </summary>
        private async Task HandleRestockHistoryCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(args))
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "📦 **Format:**\n\n`/riwayat_restock <nama_produk>`\n\nContoh:\n`/riwayat_restock minyak`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Cari produk
                var allProducts = await _posDbService.GetAllProductsAsync();
                var product = allProducts.FirstOrDefault(p =>
                    p.Name != null &&
                    (p.Name.ToLower().Contains(args.ToLower()) ||
                     p.Name.ToLower().Replace(" ", "").Contains(args.ToLower().Replace(" ", ""))));

                if (product == null || string.IsNullOrEmpty(product.Id))
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"❌ Produk \"{args}\" tidak ditemukan.",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Ambil riwayat restock
                var history = await _posDbService.GetRestockHistoryAsync(int.Parse(product.Id));

                if (!history.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"📦 **Riwayat Restock: {product.Name}**\n\nBelum ada riwayat restock untuk produk ini.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = $"📦 **Riwayat Restock: {product.Name}**\n\n";
                message += "| Tanggal | Dokumen | Qty | Harga | Total |\n";
                message += "|---------|---------|-----|-------|-------|\n";

                foreach (var item in history.Take(10))
                {
                    message += $"| {item.Date?.ToString("dd/MM/yy") ?? "-"} | {item.DocumentNumber ?? "-"} | {item.Quantity} | Rp {item.Price:N0} | Rp {item.Total:N0} |\n";
                }

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling riwayat restock: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil riwayat restock.",
                    cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Handler untuk /riwayat_inventory [produk]
        /// </summary>
        private async Task HandleInventoryHistoryCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            string args,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(args))
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "📦 **Format:**\n\n`/riwayat_inventory <nama_produk>`\n\nContoh:\n`/riwayat_inventory minyak`",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Cari produk
                var allProducts = await _posDbService.GetAllProductsAsync();
                var product = allProducts.FirstOrDefault(p =>
                    p.Name != null &&
                    (p.Name.ToLower().Contains(args.ToLower()) ||
                     p.Name.ToLower().Replace(" ", "").Contains(args.ToLower().Replace(" ", ""))));

                if (product == null || string.IsNullOrEmpty(product.Id))
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"❌ Produk \"{args}\" tidak ditemukan.",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Ambil riwayat inventory
                var history = await _posDbService.GetInventoryHistoryAsync(int.Parse(product.Id));

                if (!history.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"📦 **Riwayat Inventory: {product.Name}**\n\nBelum ada riwayat inventory untuk produk ini.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = $"📦 **Riwayat Inventory: {product.Name}**\n\n";
                message += "| Tanggal | Dokumen | Perubahan |\n";
                message += "|---------|---------|-----------|\n";

                foreach (var item in history.Take(10))
                {
                    string change = item.QuantityChange >= 0 ? $"+{item.QuantityChange}" : $"{item.QuantityChange}";
                    message += $"| {item.Date?.ToString("dd/MM/yy") ?? "-"} | {item.DocumentNumber ?? "-"} | {change} |\n";
                }

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling riwayat inventory: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil riwayat inventory.",
                    cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Handler untuk /rekomendasi_restock - Auto-recommend restock berdasarkan stok rendah
        /// </summary>
        private async Task HandleAutoRestockRecommendationCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

                // Ambil rekomendasi restock otomatis
                var recommendations = await _posDbService.GetAutoRestockRecommendationsAsync(10);

                if (!recommendations.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "✅ **Tidak ada rekomendasi restock saat ini.**\n\nSemua produk dengan stok rendah sudah di-restock dalam 7 hari terakhir.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = "📦 **REKOMENDASI RESTOCK OTOMATIS**\n\n";
                message += "Produk berikut perlu di-restock:\n\n";

                foreach (var rec in recommendations.Take(10))
                {
                    message += $"• **{rec.ProductName}**\n";
                    message += $"  Stok: {rec.CurrentStock} {rec.Unit} → Rekomendasi: +{rec.RecommendedQty} {rec.Unit}\n";
                    if (rec.CostPrice > 0)
                    {
                        decimal totalCost = rec.RecommendedQty * rec.CostPrice;
                        message += $"  Estimasi Modal: Rp {totalCost:N0}\n";
                    }
                    message += "\n";
                }

                message += "💡 *Gunakan `/restock <produk> <qty> [harga]` untuk restock.*";

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling rekomendasi restock: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil rekomendasi restock.",
                    cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Handler untuk /notifikasi_stok - Notifikasi manual produk stok habis/minus
        /// </summary>
        private async Task HandleStockNotificationCommandAsync(
            ITelegramBotClient botClient,
            long chatId,
            CancellationToken cancellationToken)
        {
            if (_posDbService == null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Database pos.db belum dikonfigurasi.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

                // Ambil produk dengan stok kritis
                var criticalProducts = await _posDbService.GetCriticalStockProductsAsync();

                if (!criticalProducts.Any())
                {
                    await botClient.SendTextMessageAsync(
                        chatId,
                        "✅ **Semua stok aman!**\n\nTidak ada produk dengan stok habis atau minus.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                string message = "🚨 **NOTIFIKASI STOK KRITIS**\n\n";
                message += $"Ditemukan {criticalProducts.Count} produk dengan stok habis/minus:\n\n";

                foreach (var product in criticalProducts.Take(10))
                {
                    string status = product.Stock < 0 ? "🔴 MINUS" : "⚠️ HABIS";
                    message += $"{status} **{product.Name}**: {product.Stock} {product.Unit}\n";
                }

                if (criticalProducts.Count > 10)
                {
                    message += $"\n... dan {criticalProducts.Count - 10} produk lainnya.";
                }

                message += "\n\n💡 *Gunakan `/restock <produk> <qty> [harga]` untuk restock.*";

                await botClient.SendTextMessageAsync(
                    chatId,
                    message,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error handling notifikasi stok: {ex.Message}",
                    "Command",
                    ex.ToString());

                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚠️ Terjadi kesalahan saat mengambil notifikasi stok.",
                    cancellationToken: cancellationToken);
            }
        }

        #endregion

        public async Task SendMessageAsync(long chatId, string message, ParseMode parseMode = ParseMode.Markdown)
        {
            if (_botClient != null && _isRunning)
            {
                await _botClient.SendTextMessageAsync(chatId, message, parseMode: parseMode);
            }
        }
    }
}
