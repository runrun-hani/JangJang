using System.IO;
using System.Text.Json;

namespace JangJang.Core;

public class WorkLog
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JangJang");
    private static readonly string LogPath = Path.Combine(LogDir, "worklog.json");

    public Dictionary<string, int> DailySeconds { get; set; } = new();

    public int TodaySeconds
    {
        get => DailySeconds.TryGetValue(TodayKey, out var s) ? s : 0;
        set => DailySeconds[TodayKey] = value;
    }

    public int TotalSeconds => DailySeconds.Values.Sum();

    private static string TodayKey => DateTime.Now.ToString("yyyy-MM-dd");

    public void AddSecond()
    {
        DailySeconds[TodayKey] = TodaySeconds + 1;
    }

    public static string FormatTime(int totalSeconds)
    {
        var h = totalSeconds / 3600;
        var m = (totalSeconds % 3600) / 60;
        var s = totalSeconds % 60;
        return h > 0 ? $"{h}시간 {m}분" : m > 0 ? $"{m}분 {s}초" : $"{s}초";
    }

    public static WorkLog Load()
    {
        try
        {
            if (File.Exists(LogPath))
            {
                var json = File.ReadAllText(LogPath);
                return JsonSerializer.Deserialize<WorkLog>(json) ?? new WorkLog();
            }
        }
        catch { }
        return new WorkLog();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LogPath, json);
        }
        catch { }
    }
}
