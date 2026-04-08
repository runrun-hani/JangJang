namespace JangJang.Core;

public enum IdlePreset
{
    Strict,   // 5분
    Normal,   // 10분
    Relaxed,  // 15분
    Custom
}

public static class IdlePresetExtensions
{
    public static int ToMinutes(this IdlePreset preset) => preset switch
    {
        IdlePreset.Strict => 5,
        IdlePreset.Normal => 10,
        IdlePreset.Relaxed => 15,
        _ => 10
    };

    public static string ToDisplayName(this IdlePreset preset) => preset switch
    {
        IdlePreset.Strict => "엄격 (5분)",
        IdlePreset.Normal => "보통 (10분)",
        IdlePreset.Relaxed => "느긋 (15분)",
        IdlePreset.Custom => "사용자 지정",
        _ => preset.ToString()
    };
}
