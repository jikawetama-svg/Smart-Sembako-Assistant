using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartSembakoAssistant.Models;
using SmartSembakoAssistant.Services;

namespace SmartSembakoAssistant.Views
{
    public partial class LogsView : UserControl
    {
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        
        private ObservableCollection<LogEntry> _logs = new();

        public LogsView(
            DatabaseService databaseService,
            LoggingService loggingService)
        {
            InitializeComponent();

            _databaseService = databaseService;
            _loggingService = loggingService;

            DgLogs.ItemsSource = _logs;
            
            // Load logs setelah UI fully loaded
            Loaded += async (s, e) => await LoadLogsAsync();
        }

        public async Task LoadDataAsync()
        {
            await LoadLogsAsync();
        }

        private async Task LoadLogsAsync(string? level = null, string? category = null)
        {
            try
            {
                // Null check untuk semua UI elements
                if (DgLogs == null)
                {
                    return;
                }

                var logs = await _loggingService.GetLogsAsync(category, level, 500);

                _logs.Clear();
                foreach (var log in logs)
                {
                    _logs.Add(log);
                }

                // Update stats dengan null check
                if (TxtLogCount != null)
                    TxtLogCount.Text = $"{logs.Count} logs";
                if (TxtInfoCount != null)
                    TxtInfoCount.Text = logs.Count(l => l.Level == "Info").ToString();
                if (TxtWarningCount != null)
                    TxtWarningCount.Text = logs.Count(l => l.Level == "Warning").ToString();
                if (TxtErrorCount != null)
                    TxtErrorCount.Text = logs.Count(l => l.Level == "Error" || l.Level == "Critical").ToString();
                if (TxtDisplayCount != null)
                    TxtDisplayCount.Text = $"Menampilkan: {logs.Count} logs";
            }
            catch (Exception ex)
            {
                // Log error tapi tidak crash
                try
                {
                    await _loggingService.LogErrorAsync($"Error loading logs: {ex.Message}", "UI", ex.ToString());
                }
                catch
                {
                    // Ignore jika logging juga gagal
                }
                
                MessageBox.Show($"❌ Error loading logs:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CmbLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategory == null) return;
            
            var selectedLevel = (CmbLevel.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var selectedCategory = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString();
            
            string? level = selectedLevel == "Semua" ? null : selectedLevel;
            string? category = selectedCategory == "Semua" ? null : selectedCategory;
            
            await LoadLogsAsync(level, category);
        }

        private async void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLevel == null) return;
            
            var selectedLevel = (CmbLevel.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var selectedCategory = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString();
            
            string? level = selectedLevel == "Semua" ? null : selectedLevel;
            string? category = selectedCategory == "Semua" ? null : selectedCategory;
            
            await LoadLogsAsync(level, category);
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await _loggingService.ExportLogsToCsvAsync(saveDialog.FileName);

                    MessageBox.Show($"✅ Logs berhasil di-export ke:\n\n{saveDialog.FileName}",
                        "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error exporting logs:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearOldLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("🗑️ Hapus semua logs yang lebih dari 30 hari?\n\nTindakan ini tidak bisa dibatalkan.",
                    "Clear Old Logs", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    int deleted = await _loggingService.ClearOldLogsAsync(30);
                    MessageBox.Show($"✅ {deleted} logs lama berhasil dibersihkan!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Reload logs
                    await LoadLogsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearAllLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("⚠️ HAPUS SEMUA LOGS?\n\nTindakan ini akan menghapus SEMUA log dan tidak bisa dibatalkan!\n\nApakah Anda yakin?",
                    "Clear ALL Logs", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    // Double confirmation
                    var confirmResult = MessageBox.Show("Anda benar-benar yakin ingin menghapus SEMUA logs?",
                        "Final Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (confirmResult == MessageBoxResult.Yes)
                    {
                        int deleted = await _loggingService.ClearAllLogsAsync();
                        MessageBox.Show($"✅ {deleted} logs berhasil dihapus!", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);

                        // Reload logs
                        await LoadLogsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
