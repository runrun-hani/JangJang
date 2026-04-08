using System.Threading;
using System.Windows;
using JangJang.Core;
using JangJang.TrayIcon;
using JangJang.ViewModels;
using JangJang.Views;

namespace JangJang;

public partial class App : Application
{
    private Mutex? _mutex;
    private ActivityMonitor? _monitor;
    private TrayIconManager? _trayManager;
    private NotificationManager? _notificationManager;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 단일 인스턴스 보장
        _mutex = new Mutex(true, "JangJang_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("장장이 이미 실행 중입니다!", "장장", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        _monitor = new ActivityMonitor(settings);

        var viewModel = new PetViewModel(_monitor, settings);
        var petWindow = new PetWindow(viewModel, settings);

        _trayManager = new TrayIconManager(_monitor, petWindow, settings);
        _notificationManager = new NotificationManager(_monitor);

        petWindow.Show();
        _monitor.Start();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _notificationManager?.Dispose();
        _monitor?.Dispose();
        _trayManager?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
