using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace JangJang.Core;

public class NotificationManager : IDisposable
{
    private readonly ActivityMonitor _monitor;
    private readonly DispatcherTimer _notifyTimer;
    private bool _isAnnoyed;

    public NotificationManager(ActivityMonitor monitor)
    {
        _monitor = monitor;
        _notifyTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _notifyTimer.Tick += OnNotifyTick;

        _monitor.StateUpdated += OnStateUpdated;

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    private void OnStateUpdated(PetState state, double annoyance)
    {
        if (state == PetState.Annoyed)
        {
            if (!_isAnnoyed)
            {
                _isAnnoyed = true;
                ShowToast("뭐하냐?");
                _notifyTimer.Start();
            }
        }
        else
        {
            if (_isAnnoyed)
            {
                _isAnnoyed = false;
                _notifyTimer.Stop();
            }
        }
    }

    private void OnNotifyTick(object? sender, EventArgs e)
    {
        ShowToast("뭐하냐?");
    }

    private static void ShowToast(string message)
    {
        new ToastContentBuilder()
            .AddText("자캐 타이머")
            .AddText(message)
            .AddButton(new ToastButton()
                .SetContent("확인")
                .AddArgument("action", "dismiss"))
            .AddButton(new ToastButton()
                .SetContent("알림 중지")
                .AddArgument("action", "snooze"))
            .Show();
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var toastArgs = ToastArguments.Parse(args.Argument);
        if (toastArgs.TryGetValue("action", out string? action) && action == "snooze")
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _monitor.ForceSleep(TimeSpan.FromMinutes(5));
            });
        }
    }

    public void Dispose()
    {
        _notifyTimer.Stop();
        _monitor.StateUpdated -= OnStateUpdated;
    }
}
