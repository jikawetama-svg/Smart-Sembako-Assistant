using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartSembakoAssistant.Controls
{
    public enum ToastType
    {
        Success,
        Error,
        Warning,
        Info
    }

    public partial class ToastNotification : Window
    {
        private readonly int _autoCloseDelayMs = 3000;

        public ToastNotification(ToastType type, string title, string message)
        {
            InitializeComponent();

            ToastTitle.Text = title;
            ToastMessage.Text = message;

            // Set colors based on toast type
            switch (type)
            {
                case ToastType.Success:
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5"));
                    ToastIcon.Text = "✅";
                    break;
                case ToastType.Error:
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
                    ToastIcon.Text = "❌";
                    break;
                case ToastType.Warning:
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                    ToastIcon.Text = "⚠️";
                    break;
                case ToastType.Info:
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                    ToastIcon.Text = "ℹ️";
                    break;
            }
        }

        private void ToastNotification_Loaded(object sender, RoutedEventArgs e)
        {
            // Slide-in animation from top
            var animation = new DoubleAnimation
            {
                From = -100,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(TopProperty, animation);

            // Auto-close after delay
            _ = AutoCloseAsync();
        }

        private async Task AutoCloseAsync()
        {
            await Task.Delay(_autoCloseDelayMs);

            // Fade-out animation
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Fade-out animation on manual close
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(150)
            };

            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    /// <summary>
    /// Helper class to show toast notifications
    /// </summary>
    public static class ToastHelper
    {
        public static void ShowSuccess(string title, string message, Window? owner = null)
        {
            ShowToast(ToastType.Success, title, message, owner);
        }

        public static void ShowError(string title, string message, Window? owner = null)
        {
            ShowToast(ToastType.Error, title, message, owner);
        }

        public static void ShowWarning(string title, string message, Window? owner = null)
        {
            ShowToast(ToastType.Warning, title, message, owner);
        }

        public static void ShowInfo(string title, string message, Window? owner = null)
        {
            ShowToast(ToastType.Info, title, message, owner);
        }

        private static void ShowToast(ToastType type, string title, string message, Window? owner = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new ToastNotification(type, title, message);

                // Always position toast at top-right corner
                var workArea = SystemParameters.WorkArea;
                toast.Left = workArea.Right - toast.Width - 20;
                toast.Top = workArea.Top + 20;
                toast.Owner = Application.Current.MainWindow;

                toast.Show();
            });
        }
    }
}
