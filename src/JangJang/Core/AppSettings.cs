using System.IO;
using System.Text.Json;

namespace JangJang.Core;

public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JangJang");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public IdlePreset IdlePreset { get; set; } = IdlePreset.Normal;
    public int CustomIdleMinutes { get; set; } = 10;
    public double PetSize { get; set; } = 1.0;
    public bool StartWithWindows { get; set; }
    public double PetPositionX { get; set; } = double.NaN;
    public double PetPositionY { get; set; } = double.NaN;
    public string? PetImagePath { get; set; }
    public bool GrowWhenAnnoyed { get; set; } = true;
    public double MaxGrowScale { get; set; } = 2.0;
    public string TargetProcessName { get; set; } = "CLIPStudioPaint";
    public string TargetDisplayName { get; set; } = "Clip Studio Paint";

    public int IdleThresholdSeconds => IdlePreset == IdlePreset.Custom
        ? CustomIdleMinutes * 60
        : IdlePreset.ToMinutes() * 60;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
