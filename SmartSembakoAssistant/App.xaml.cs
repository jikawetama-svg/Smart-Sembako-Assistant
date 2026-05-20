using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SmartSembakoAssistant.Helpers;

namespace SmartSembakoAssistant;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\SmartSembakoAssistant.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Smart Sembako Assistant sudah berjalan di perangkat ini.",
                "Aplikasi Sudah Aktif",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string errorMessage = $"Terjadi kesalahan pada aplikasi:\n\n{e.Exception.Message}\n\n" +
                             $"Silakan cek file log untuk detail lebih lanjut.";

        MessageBox.Show(
            errorMessage,
            "Error - Smart Sembako Assistant",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            try
            {
                string logPath = RuntimePaths.LogsDirectory;
                string logFile = Path.Combine(logPath, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(logFile, $"CRASH LOG\n{DateTime.Now}\n\n{ex}");
            }
            catch
            {
                // Ignore jika tidak bisa buat log file.
            }
        }
    }
}
