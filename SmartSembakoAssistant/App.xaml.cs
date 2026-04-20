using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SmartSembakoAssistant;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Global error handlers
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log error
        string errorMessage = $"Terjadi kesalahan pada aplikasi:\n\n{e.Exception.Message}\n\n" +
                             $"Silakan cek file log untuk detail lebih lanjut.";
        
        MessageBox.Show(
            errorMessage,
            "Error - Smart Sembako Assistant",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        
        // Mark as handled untuk mencegah crash
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            // Log ke file jika memungkinkan
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "logs");
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);
                }
                
                string logFile = Path.Combine(logPath, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(logFile, $"CRASH LOG\n{DateTime.Now}\n\n{ex}");
            }
            catch
            {
                // Ignore jika tidak bisa buat log file
            }
        }
    }
}

