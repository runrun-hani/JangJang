using System.Windows.Threading;
using JangJang.Core.Persona;
using Microsoft.Toolkit.Uwp.Notifications;

namespace JangJang.Core;

public class NotificationManager : IDisposable
{
    private const string DefaultTitle = "자캐 타이머";
    private const string FallbackMessage = "뭐하냐?";

    private readonly ActivityMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _notifyTimer;
    private bool _isAnnoyed;

    public NotificationManager(ActivityMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
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
                ShowToast();
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
        ShowToast();
    }

    private void ShowToast()
    {
        var message = GetAnnoyedLine();
        var title = ResolveTitle();

        new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .AddButton(new ToastButton()
                .SetContent("확인")
                .AddArgument("action", "dismiss"))
            .AddButton(new ToastButton()
                .SetContent("알림 중지")
                .AddArgument("action", "snooze"))
            .Show();
    }

    private string GetAnnoyedLine()
    {
        try
        {
            var line = Dialogue.GetLine(
                PetState.Annoyed,
                _monitor.AnnoyanceLevel,
                _monitor.WorkLog.TodaySeconds);
            return string.IsNullOrWhiteSpace(line) ? FallbackMessage : line;
        }
        catch
        {
            return FallbackMessage;
        }
    }

    private string ResolveTitle()
    {
        if (!_settings.PersonaEnabled) return DefaultTitle;
        if (string.IsNullOrEmpty(_settings.ActivePersonaId)) return DefaultTitle;

        try
        {
            var persona = PersonaStore.Load(_settings.ActivePersonaId);
            var name = persona?.Name;
            return string.IsNullOrWhiteSpace(name) ? DefaultTitle : name;
        }
        catch
        {
            return DefaultTitle;
        }
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
