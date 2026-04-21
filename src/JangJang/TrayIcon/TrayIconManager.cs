using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using JangJang.Core;
using JangJang.Views;
using DrawColor = System.Drawing.Color;

namespace JangJang.TrayIcon;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ActivityMonitor _monitor;
    private readonly PetWindow _petWindow;
    private readonly AppSettings _settings;
    private readonly ContextMenuStrip _contextMenu;
    private Icon? _currentIcon;

    public TrayIconManager(ActivityMonitor monitor, PetWindow petWindow, AppSettings settings)
    {
        _monitor = monitor;
        _petWindow = petWindow;
        _settings = settings;
        _contextMenu = CreateContextMenu();

        _currentIcon = CreateIcon(DrawColor.Gray);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "자캐 타이머 - zzZ...",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) =>
        {
            _petWindow.Show();
            _petWindow.Activate();
        };

        _monitor.StateUpdated += OnStateUpdated;
    }

    private void OnStateUpdated(PetState state, double annoyance)
    {
        var (color, text) = state switch
        {
            PetState.Happy => (DrawColor.Gold, "자캐 타이머 - 열심히 일하는 중!"),
            PetState.Alert => (DrawColor.Orange, "자캐 타이머 - ...뭐 하는 거야?"),
            PetState.Annoyed => (DrawColor.Red, "자캐 타이머 - 일 안 해?!"),
            PetState.WakeUp => (DrawColor.LightBlue, "자캐 타이머 - ..."),
            _ => (DrawColor.Gray, "자캐 타이머 - zzZ...")
        };

        var oldIcon = _currentIcon;
        _currentIcon = CreateIcon(color);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;

        // 이전 아이콘 해제
        if (oldIcon != null)
        {
            DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon CreateIcon(DrawColor color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);
        g.DrawEllipse(Pens.Black, 1, 1, 14, 14);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("설정", null, (_, _) =>
        {
            var sw = new SettingsWindow(_settings) { Owner = _petWindow };
            if (sw.ShowDialog() == true)
                _petWindow.ApplySettingsFromTray();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) =>
        {
            _settings.Save();
            Application.Current.Shutdown();
        });
        return menu;
    }

    public void Dispose()
    {
        _monitor.StateUpdated -= OnStateUpdated;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        if (_currentIcon != null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
    }
}
