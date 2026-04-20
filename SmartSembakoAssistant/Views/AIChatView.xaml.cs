using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SmartSembakoAssistant.Controls;
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

        private readonly ObservableCollection<ChatMessage> _chatMessages;
        private readonly ICollectionView _chatMessagesView;

        private string _currentMode = "Owner";
        private bool _isProcessing;

        public AIChatView(
            ConfigService configService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService,
            GroqService groqService)
        {
            try
            {
                _configService = configService;
                _databaseService = databaseService;
                _loggingService = loggingService;
                _posDbService = posDbService;
                _groqService = groqService;

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
                TxtStatus.Text = $"Mode: {_currentMode}";
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

                // Build context from POS data if available
                string? storeContext = null;
                if (_posDbService != null)
                {
                    try
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
                if (_currentMode == "General")
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
