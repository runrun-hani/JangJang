using Microsoft.Win32;

namespace JangJang.Core;

public static class AutoStartHelper
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "JangJang";

    public static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) != null;
    }
}
