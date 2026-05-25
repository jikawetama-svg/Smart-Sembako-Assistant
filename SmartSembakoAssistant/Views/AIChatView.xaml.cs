using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SmartSembakoAssistant.Controls;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class AIChatView : UserControl
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private readonly GroqService _groqService;
        private readonly ExportService _exportService;

        private readonly ObservableCollection<ChatMessage> _chatMessages;
        private readonly ICollectionView _chatMessagesView;

        private string _currentMode = "Owner";
        private bool _isProcessing;
        private DateTime? _lastSalesContextStartDate;
        private DateTime? _lastSalesContextEndDate;
        private string? _lastSalesContextLabel;

        public AIChatView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService,
            GroqService groqService,
            ExportService exportService)
        {
            try
            {
                _configService = configService;
                _databaseService = databaseService;
                _loggingService = loggingService;
                _posDbService = posDbService;
                _groqService = groqService;
                _exportService = exportService;

                InitializeComponent();

                _chatMessages = new ObservableCollection<ChatMessage>();
                _chatMessagesView = CollectionViewSource.GetDefaultView(_chatMessages);
                _chatMessagesView.Filter = null;

                LstChatMessages.ItemsSource = _chatMessagesView;

                UpdateModelStatus();
                UpdateEmptyStateVisibility();
            }
            catch (Exception ex)
            {
                var fullError = $"❌ Error initializing AI Chat:\n\n{ex.Message}\n\nInner: {ex.InnerException?.Message}\n\nStack:\n{ex.StackTrace}";

                System.IO.File.WriteAllText("AIChatError.log", fullError);

                ToastHelper.ShowError("AI Chat Error", fullError);
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void TxtMessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private void BtnClearChat_Click(object sender, RoutedEventArgs e)
        {
            ClearChat();
            ToastHelper.ShowInfo("Chat Cleared", "All messages have been removed.");
        }

        private void CmbPromptMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPromptMode.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string mode)
            {
                _currentMode = mode;
                if (TxtStatus != null)
                {
                    TxtStatus.Text = $"Mode: {_currentMode}";
                }
            }
        }

        private async Task SendMessageAsync()
        {
            if (_isProcessing) return;

            string userInput = TxtMessageInput.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            _isProcessing = true;
            BtnSend.IsEnabled = false;
            TxtMessageInput.IsEnabled = false;
            TxtStatus.Text = "Processing...";

            try
            {
                // Add user message
                var userMessage = new ChatMessage
                {
                    IsUser = true,
                    Content = userInput,
                    Timestamp = DateTime.Now
                };
                _chatMessages.Add(userMessage);

                // Clear input
                TxtMessageInput.Text = string.Empty;
                UpdateEmptyStateVisibility();
                ScrollToBottom();

                var localExportResponse = await TryHandleLocalExportIntentAsync(userInput);
                if (localExportResponse != null)
                {
                    _chatMessages.Add(new ChatMessage
                    {
                        IsUser = false,
                        Content = localExportResponse,
                        Timestamp = DateTime.Now
                    });

                    TxtStatus.Text = $"Ready | Mode: {_currentMode}";
                    return;
                }

                // Build context from POS data if available
                string? storeContext = null;
                bool isStoreDataQuestion = IsStoreDataQuestion(userInput);
                if (_posDbService != null)
                {
                    try
                    {
                        storeContext = await FetchContextBasedOnIntentAsync(userInput);
                        if (string.IsNullOrWhiteSpace(storeContext))
                        {
                        var revenue = await _posDbService.GetTodayRevenueAsync();
                        var profit = await _posDbService.GetTodayProfitAsync();
                        var lowStock = await _posDbService.GetLowStockProductsAsync(5);

                        storeContext = $"DATA REAL TOKO:\n";
                        storeContext += $"- Revenue Hari Ini: Rp {revenue:N0}\n";
                        storeContext += $"- Profit Hari Ini: Rp {profit:N0}\n";

                        if (lowStock.Count > 0)
                        {
                            storeContext += $"- Stok Rendah:\n";
                            foreach (var p in lowStock)
                            {
                                storeContext += $"  • {p.Name}: {p.Stock} {p.Unit}\n";
                            }
                        }
                    }
                        }
                    catch
                    {
                        // Ignore context errors, AI will work without it
                    }
                }

                // Build conversation history (last 8 messages)
                var history = _chatMessages
                    .TakeLast(8)
                    .Select(m => $"{(m.IsUser ? "User" : "AI")}: {m.Content}")
                    .ToList();

                // Get AI response
                string aiResponse;
                if (_currentMode == "General" && !isStoreDataQuestion)
                {
                    // Simple general mode without store context
                    aiResponse = await _groqService.SendPromptAsync(
                        "Anda adalah asisten AI yang membantu pertanyaan umum tentang toko sembako.",
                        userInput,
                        temperature: 0.7,
                        maxTokens: 800);
                }
                else
                {
                    // Owner or Kasir mode with context
                    aiResponse = await _groqService.GenerateNaturalResponseAsync(
                        userInput,
                        history,
                        userRole: _currentMode,
                        realStoreData: storeContext);
                }

                // Add AI response
                var aiMessage = new ChatMessage
                {
                    IsUser = false,
                    Content = aiResponse,
                    Timestamp = DateTime.Now
                };
                _chatMessages.Add(aiMessage);

                TxtStatus.Text = $"Ready | Mode: {_currentMode}";
            }
            catch (Exception ex)
            {
                var errorMessage = new ChatMessage
                {
                    IsUser = false,
                    Content = $"⚠️ Terjadi kesalahan: {ex.Message}\n\nSilakan coba lagi.",
                    Timestamp = DateTime.Now
                };
                _chatMessages.Add(errorMessage);

                await _loggingService.LogErrorAsync(
                    $"AI Chat error: {ex.Message}",
                    "AIChat",
                    ex.ToString());

                TxtStatus.Text = "Error - coba lagi";
            }
            finally
            {
                _isProcessing = false;
                BtnSend.IsEnabled = true;
                TxtMessageInput.IsEnabled = true;
                TxtMessageInput.Focus();
                UpdateEmptyStateVisibility();
                ScrollToBottom();
            }
        }

        private async Task<string?> TryHandleLocalExportIntentAsync(string userInput)
        {
            var inputLower = userInput.ToLowerInvariant();
            bool hasExportVerb = ContainsAny(inputLower, "ekspor", "export", "download", "unduh");
            bool hasFormatOnlyExport = ContainsAny(inputLower, "csv", "excel", "xlsx", "pdf") && _lastSalesContextStartDate.HasValue;
            if (!hasExportVerb && !hasFormatOnlyExport)
            {
                return null;
            }

            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi, jadi export belum bisa dibuat dari AI Chat.";
            }

            ExportFormat format = DetectExportFormat(inputLower);

            if (ContainsAny(inputLower, "penjualan", "omzet", "transaksi", "laporan", "csv") ||
                _lastSalesContextStartDate.HasValue)
            {
                var requestedDate = ParseDateFromText(inputLower);
                var startDate = requestedDate ?? _lastSalesContextStartDate ?? DateTime.Today;
                var endDate = requestedDate ?? _lastSalesContextEndDate ?? startDate;
                var label = requestedDate.HasValue
                    ? $"Penjualan {requestedDate.Value:dd/MM/yyyy}"
                    : _lastSalesContextLabel ?? $"Penjualan {startDate:dd/MM/yyyy}";

                return await ExportSalesFromChatAsync(startDate, endDate, label, format);
            }

            return "Mau ekspor apa?\n- ekspor penjualan ke csv\n- ekspor stok/produk dari halaman Stock Monitoring\n- export Excel/PDF native masuk plan lanjutan.";
        }

        private async Task<string> ExportSalesFromChatAsync(DateTime startDate, DateTime endDate, string label, ExportFormat format)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var items = await _posDbService.GetSalesLineItemsAsync(startDate, endDate);
            if (items.Count == 0)
            {
                return $"Belum ada data penjualan untuk {label.ToLowerInvariant()}.";
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = GetExportFilter(format),
                DefaultExt = GetExportExtension(format),
                FileName = $"penjualan_{startDate:yyyyMMdd}_{DateTime.Now:HHmmss}.{GetExportExtension(format)}"
            };

            if (saveDialog.ShowDialog() != true)
            {
                return "Export dibatalkan.";
            }

            var result = await _exportService.ExportSalesAsync(
                items,
                saveDialog.FileName,
                format,
                label);

            _lastSalesContextStartDate = startDate;
            _lastSalesContextEndDate = endDate;
            _lastSalesContextLabel = label;

            return result.Success
                ? $"{result.Message}\n{result.FilePath}"
                : $"Export gagal: {result.Message}";
        }

        private static ExportFormat DetectExportFormat(string inputLower)
        {
            if (ContainsAny(inputLower, "excel", "xlsx")) return ExportFormat.Excel;
            if (ContainsAny(inputLower, "pdf")) return ExportFormat.Pdf;
            return ExportFormat.Csv;
        }

        private static string GetExportExtension(ExportFormat format)
        {
            return format switch
            {
                ExportFormat.Excel => "xlsx",
                ExportFormat.Pdf => "pdf",
                _ => "csv"
            };
        }

        private static string GetExportFilter(ExportFormat format)
        {
            return format switch
            {
                ExportFormat.Excel => "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                ExportFormat.Pdf => "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                _ => "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };
        }

        private static bool IsStoreDataQuestion(string input)
        {
            var text = input.ToLowerInvariant();
            string[] keywords =
            {
                "penjualan", "omzet", "revenue", "profit", "transaksi", "laporan",
                "stok", "barang", "produk", "harga", "piutang", "hutang", "kredit"
            };

            return keywords.Any(text.Contains);
        }

        private async Task<string> FetchContextBasedOnIntentAsync(string userInput)
        {
            if (_posDbService == null)
            {
                return string.Empty;
            }

            var inputLower = userInput.ToLowerInvariant();

            if (ContainsAny(inputLower, "penjualan", "omzet", "revenue", "profit", "transaksi", "laporan"))
            {
                var targetDate = ParseDateFromText(inputLower) ?? DateTime.Today;
                _lastSalesContextStartDate = targetDate;
                _lastSalesContextEndDate = targetDate;
                _lastSalesContextLabel = $"Penjualan {targetDate:dd/MM/yyyy}";

                var revenueTask = _posDbService.GetSalesRevenueAsync(targetDate, targetDate);
                var profitTask = _posDbService.GetSalesProfitAsync(targetDate, targetDate);
                var transactionTask = _posDbService.GetSalesTransactionCountAsync(targetDate, targetDate);
                var topProductsTask = _posDbService.GetTopSellingProductsAsync(targetDate, targetDate, 5);

                await Task.WhenAll(revenueTask, profitTask, transactionTask, topProductsTask);

                var revenue = await revenueTask;
                var profit = await profitTask;
                var transactionCount = await transactionTask;
                var builder = new System.Text.StringBuilder();
                builder.AppendLine($"DATA REAL TOKO - PENJUALAN {targetDate:dd/MM/yyyy}:");
                builder.AppendLine($"- Total Omzet: Rp {revenue:N0}");
                builder.AppendLine($"- Total Profit: Rp {profit:N0}");
                builder.AppendLine($"- Jumlah Transaksi: {transactionCount:N0}");

                var topProducts = await topProductsTask;
                if (topProducts.Count > 0)
                {
                    builder.AppendLine("- Produk Terlaris:");
                    foreach (var product in topProducts)
                    {
                        builder.AppendLine($"  - {product.ProductName}: {product.QuantitySold:N0} {product.Unit}, omzet Rp {product.Revenue:N0}");
                    }
                }

                return builder.ToString();
            }

            if (ContainsAny(inputLower, "stok", "barang", "produk", "harga"))
            {
                var keyword = ExtractKeywordAfter(inputLower, "stok", "barang", "produk", "harga", "cek", "berapa");
                var products = await _posDbService.GetAllProductsAsync();

                var matches = products
                    .Where(product => !string.IsNullOrWhiteSpace(product.Name))
                    .Where(product => string.IsNullOrWhiteSpace(keyword) ||
                                      product.Name!.ToLowerInvariant().Contains(keyword) ||
                                      product.Sku?.ToLowerInvariant().Contains(keyword) == true)
                    .OrderBy(product => product.Stock ?? decimal.MinValue)
                    .Take(8)
                    .ToList();

                if (matches.Count == 0)
                {
                    return $"DATA REAL TOKO - STOK:\n- Produk dengan kata kunci '{keyword}' tidak ditemukan.";
                }

                var builder = new System.Text.StringBuilder();
                builder.AppendLine($"DATA REAL TOKO - STOK {(string.IsNullOrWhiteSpace(keyword) ? "TERKAIT" : keyword.ToUpperInvariant())}:");
                foreach (var product in matches)
                {
                    builder.AppendLine($"- {product.Name}: stok {product.Stock} {product.Unit}, jual Rp {product.SellingPrice:N0}, status {product.StockStatusText}");
                }

                return builder.ToString();
            }

            if (ContainsAny(inputLower, "piutang", "hutang", "kredit"))
            {
                var customerKeyword = ExtractKeywordAfter(inputLower, "piutang", "hutang", "kredit", "cek", "customer", "pelanggan");
                var receivables = await _posDbService.GetCustomerReceivablesAsync();
                var matches = receivables
                    .Where(item => string.IsNullOrWhiteSpace(customerKeyword) ||
                                   item.CustomerName.ToLowerInvariant().Contains(customerKeyword))
                    .Take(8)
                    .ToList();

                if (matches.Count == 0)
                {
                    return $"DATA REAL TOKO - PIUTANG:\n- Catatan piutang untuk '{customerKeyword}' tidak ditemukan di database saat ini.";
                }

                var builder = new System.Text.StringBuilder();
                builder.AppendLine($"DATA REAL TOKO - PIUTANG {(string.IsNullOrWhiteSpace(customerKeyword) ? "PELANGGAN" : customerKeyword.ToUpperInvariant())}:");
                foreach (var item in matches)
                {
                    builder.AppendLine($"- {item.CustomerName}: Rp {item.TotalOwed:N0}, {item.InvoiceCount:N0} invoice, transaksi terakhir {item.LastTransactionDate:dd/MM/yyyy}");
                }

                return builder.ToString();
            }

            return string.Empty;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(text.Contains);
        }

        private static string ExtractKeywordAfter(string input, params string[] stopWords)
        {
            var cleaned = input;
            foreach (var word in stopWords)
            {
                cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(word)}\b", " ", RegexOptions.IgnoreCase);
            }

            cleaned = Regex.Replace(cleaned, @"\b(berapa|dong|tolong|info|data|nya|ibu|pak|bapak)\b", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        private static DateTime? ParseDateFromText(string input)
        {
            if (input.Contains("hari ini")) return DateTime.Today;
            if (input.Contains("kemarin")) return DateTime.Today.AddDays(-1);

            var monthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["jan"] = 1, ["januari"] = 1,
                ["feb"] = 2, ["februari"] = 2,
                ["mar"] = 3, ["maret"] = 3,
                ["apr"] = 4, ["april"] = 4,
                ["mei"] = 5,
                ["jun"] = 6, ["juni"] = 6,
                ["jul"] = 7, ["juli"] = 7,
                ["agu"] = 8, ["agustus"] = 8,
                ["sep"] = 9, ["september"] = 9,
                ["okt"] = 10, ["oktober"] = 10,
                ["nov"] = 11, ["november"] = 11,
                ["des"] = 12, ["desember"] = 12
            };

            var textDate = Regex.Match(input, @"\b(?<day>\d{1,2})\s+(?<month>[a-zA-Z]+)(?:\s+(?<year>\d{4}))?\b");
            if (textDate.Success && monthMap.TryGetValue(textDate.Groups["month"].Value, out var month))
            {
                var day = int.Parse(textDate.Groups["day"].Value, CultureInfo.InvariantCulture);
                var year = textDate.Groups["year"].Success
                    ? int.Parse(textDate.Groups["year"].Value, CultureInfo.InvariantCulture)
                    : DateTime.Today.Year;

                if (DateTime.TryParse($"{year}-{month:00}-{day:00}", out var parsed))
                {
                    return parsed.Date;
                }
            }

            var numericDate = Regex.Match(input, @"\b(?<day>\d{1,2})[/-](?<month>\d{1,2})(?:[/-](?<year>\d{2,4}))?\b");
            if (numericDate.Success)
            {
                var day = int.Parse(numericDate.Groups["day"].Value, CultureInfo.InvariantCulture);
                var numericMonth = int.Parse(numericDate.Groups["month"].Value, CultureInfo.InvariantCulture);
                var year = numericDate.Groups["year"].Success
                    ? int.Parse(numericDate.Groups["year"].Value, CultureInfo.InvariantCulture)
                    : DateTime.Today.Year;
                if (year < 100) year += 2000;

                if (DateTime.TryParse($"{year}-{numericMonth:00}-{day:00}", out var parsed))
                {
                    return parsed.Date;
                }
            }

            return null;
        }

        private void ClearChat()
        {
            _chatMessages.Clear();
            UpdateEmptyStateVisibility();
            TxtStatus.Text = $"Chat cleared | Mode: {_currentMode}";
        }

        private void ScrollToBottom()
        {
            if (LstChatMessages.Items.Count > 0)
            {
                LstChatMessages.ScrollIntoView(LstChatMessages.Items[LstChatMessages.Items.Count - 1]);
            }
        }

        private void UpdateEmptyStateVisibility()
        {
            EmptyStatePanel.Visibility = _chatMessages.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateModelStatus()
        {
            var config = _configService.Config;
            if (config?.Groq != null && !string.IsNullOrEmpty(config.Groq.ApiKey) && config.Groq.ApiKey != "YOUR_GROQ_API_KEY")
            {
                TxtModelStatus.Text = config.Groq.Model ?? "llama-3.1-8b-instant";
                StatusDot.Fill = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
            }
            else
            {
                TxtModelStatus.Text = "Not Configured";
                StatusDot.Fill = (Brush)new BrushConverter().ConvertFrom("#EF4444")!;
            }
        }
    }

    /// <summary>
    /// Represents a chat message in the conversation
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private bool _isUser;
        private string _content = string.Empty;
        private DateTime _timestamp;

        public bool IsUser
        {
            get => _isUser;
            set
            {
                _isUser = value;
                OnPropertyChanged();
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
