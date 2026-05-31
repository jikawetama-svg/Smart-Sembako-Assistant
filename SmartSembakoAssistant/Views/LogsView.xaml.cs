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
        private const int PageSize = 200;

        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;

        private ObservableCollection<LogEntry> _logs = new();
        private CancellationTokenSource? _loadLogsCts;
        private string? _currentLevel;
        private string? _currentCategory;
        private bool _hasMoreLogs;
        private bool _isBusy;
        private DateTime _lastLoadedAt;

        public LogsView(
            DatabaseService databaseService,
            LoggingService loggingService)
        {
            InitializeComponent();

            _databaseService = databaseService;
            _loggingService = loggingService;

            DgLogs.ItemsSource = _logs;

            Loaded += async (s, e) => await LoadLogsIfStaleAsync();
        }

        public async Task LoadDataAsync()
        {
            await LoadLogsIfStaleAsync();
        }

        private async Task LoadLogsIfStaleAsync()
        {
            if (_logs.Count > 0 && DateTime.Now - _lastLoadedAt < TimeSpan.FromSeconds(15))
            {
                UpdateStats();
                return;
            }

            await LoadLogsAsync(GetSelectedLevel(), GetSelectedCategory());
        }

        private async Task LoadLogsAsync(
            string? level = null,
            string? category = null,
            bool append = false,
            bool debounce = false)
        {
            if (DgLogs == null)
            {
                return;
            }

            _loadLogsCts?.Cancel();
            _loadLogsCts?.Dispose();
            _loadLogsCts = new CancellationTokenSource();
            var token = _loadLogsCts.Token;

            _currentLevel = level;
            _currentCategory = category;

            try
            {
                SetBusy(true, append ? "Memuat log berikutnya..." : "Memuat log...");

                if (debounce)
                {
                    await Task.Delay(150, token);
                }

                long? beforeId = append ? _logs.LastOrDefault()?.Id : null;
                var fetchedLogs = await _loggingService.GetLogsAsync(
                    category,
                    level,
                    PageSize + 1,
                    beforeId,
                    token);

                var pageLogs = fetchedLogs.Take(PageSize).ToList();
                _hasMoreLogs = fetchedLogs.Count > PageSize;

                if (append)
                {
                    foreach (var log in pageLogs)
                    {
                        _logs.Add(log);
                    }
                }
                else
                {
                    _logs = new ObservableCollection<LogEntry>(pageLogs);
                    DgLogs.ItemsSource = _logs;
                }

                UpdateStats();
                _lastLoadedAt = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                // Filter changes intentionally cancel the previous load.
            }
            catch (Exception ex)
            {
                try
                {
                    await _loggingService.LogErrorAsync($"Error loading logs: {ex.Message}", "UI", ex.ToString());
                }
                catch
                {
                    // Ignore jika logging juga gagal.
                }

                MessageBox.Show($"Error loading logs:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    SetBusy(false);
                    UpdateStats();
                }
            }
        }

        private async void CmbLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategory == null)
            {
                return;
            }

            await LoadLogsAsync(GetSelectedLevel(), GetSelectedCategory(), debounce: true);
        }

        private async void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLevel == null)
            {
                return;
            }

            await LoadLogsAsync(GetSelectedLevel(), GetSelectedCategory(), debounce: true);
        }

        private async void BtnLoadMore_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || !_hasMoreLogs)
            {
                return;
            }

            await LoadLogsAsync(_currentLevel, _currentCategory, append: true);
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

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
                    SetBusy(true, "Export CSV...");
                    var progress = new Progress<int>(count => TxtLogStatus.Text = $"Export {count} logs...");
                    await _loggingService.ExportLogsToCsvAsync(
                        saveDialog.FileName,
                        _currentCategory,
                        _currentLevel,
                        progress);
                    TxtLogStatus.Text = $"Export selesai: {saveDialog.FileName}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting logs:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnClearOldLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            var result = MessageBox.Show(
                "Hapus semua logs yang lebih dari 30 hari?\n\nTindakan ini tidak bisa dibatalkan.",
                "Clear Old Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true, "Menghapus log lama...");
                var progress = new Progress<int>(deleted => TxtLogStatus.Text = $"Menghapus {deleted} logs...");
                var deleted = await _loggingService.ClearOldLogsChunkedAsync(30, progress);

                await LoadLogsAsync(_currentLevel, _currentCategory);
                TxtLogStatus.Text = $"{deleted.DeletedCount} logs lama dihapus ({deleted.BatchCount} batch).";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnClearAllLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            var result = MessageBox.Show(
                "Hapus semua logs?\n\nTindakan ini akan menghapus semua log dan tidak bisa dibatalkan.",
                "Clear All Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true, "Menghapus semua log...");
                var progress = new Progress<int>(deleted => TxtLogStatus.Text = $"Menghapus {deleted} logs...");
                var deleted = await _loggingService.ClearAllLogsChunkedAsync(progress);

                await LoadLogsAsync(_currentLevel, _currentCategory);
                TxtLogStatus.Text = $"{deleted.DeletedCount} logs dihapus ({deleted.BatchCount} batch).";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string? GetSelectedLevel()
        {
            var selectedLevel = (CmbLevel.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return selectedLevel == "Semua" ? null : selectedLevel;
        }

        private string? GetSelectedCategory()
        {
            var selectedCategory = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return selectedCategory == "Semua" ? null : selectedCategory;
        }

        private void UpdateStats()
        {
            if (TxtLogCount != null)
            {
                TxtLogCount.Text = $"{_logs.Count} logs";
            }

            if (TxtInfoCount != null)
            {
                TxtInfoCount.Text = _logs.Count(l => l.Level == "Info").ToString();
            }

            if (TxtWarningCount != null)
            {
                TxtWarningCount.Text = _logs.Count(l => l.Level == "Warning").ToString();
            }

            if (TxtErrorCount != null)
            {
                TxtErrorCount.Text = _logs.Count(l => l.Level == "Error" || l.Level == "Critical").ToString();
            }

            if (TxtDisplayCount != null)
            {
                TxtDisplayCount.Text = _hasMoreLogs
                    ? $"Menampilkan {_logs.Count} logs"
                    : $"Menampilkan semua {_logs.Count} logs";
            }

            if (TxtLogStatus != null && !_isBusy)
            {
                TxtLogStatus.Text = _hasMoreLogs ? "Masih ada log lain." : "";
            }

            if (BtnLoadMore != null)
            {
                BtnLoadMore.IsEnabled = !_isBusy && _hasMoreLogs;
            }
        }

        private void SetBusy(bool isBusy, string? status = null)
        {
            _isBusy = isBusy;

            if (CmbLevel != null)
            {
                CmbLevel.IsEnabled = !isBusy;
            }

            if (CmbCategory != null)
            {
                CmbCategory.IsEnabled = !isBusy;
            }

            if (BtnExport != null)
            {
                BtnExport.IsEnabled = !isBusy;
            }

            if (BtnLoadMore != null)
            {
                BtnLoadMore.IsEnabled = !isBusy && _hasMoreLogs;
            }

            if (TxtLogStatus != null && !string.IsNullOrWhiteSpace(status))
            {
                TxtLogStatus.Text = status;
            }
        }
    }
}
