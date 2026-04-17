using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public bool NoRestMode { get; set; }
    public double PetPositionX { get; set; } = double.NaN;
    public double PetPositionY { get; set; } = double.NaN;
    public string? PetImagePath { get; set; }
    public string? HappyImagePath { get; set; }
    public string? IdleImagePath { get; set; }
    public string? AnnoyedImagePath { get; set; }
    public string? SleepingImagePath { get; set; }
    public string? WakeUpImagePath { get; set; }
    public bool GrowWhenAnnoyed { get; set; } = true;
    public double MaxGrowScale { get; set; } = 2.0;
    public string TargetProcessName { get; set; } = "CLIPStudioPaint";
    public string TargetDisplayName { get; set; } = "Clip Studio Paint";

    /// <summary>
    /// 자캐 페르소나 모드 활성화 여부. true이고 페르소나 데이터 + 임베딩 모델이 모두 준비되어 있을 때만
    /// PersonaDialogueProvider가 동작한다. 둘 중 하나라도 부재하면 자동으로 기본(치와와) Provider로 폴백.
    /// </summary>
    public bool PersonaEnabled { get; set; }

    /// <summary>AI 추천 API 프로바이더 (예: "gemini", "groq", "custom")</summary>
    public string SuggestionApiProvider { get; set; } = "gemini";

    /// <summary>DPAPI로 암호화된 API 키 (base64). JSON 직렬화 대상.</summary>
    public string? SuggestionApiKeyProtected { get; set; }

    /// <summary>[레거시] 평문 API 키. 기존 settings.json 마이그레이션용. 로드 후 암호화로 전환.</summary>
    public string? SuggestionApiKey { get; set; }

    /// <summary>복호화된 API 키. 메모리에서만 사용, JSON에 저장되지 않음.</summary>
    [JsonIgnore]
    public string? SuggestionApiKeyDecrypted
    {
        get
        {
            if (!string.IsNullOrEmpty(SuggestionApiKeyProtected))
                return DecryptApiKey(SuggestionApiKeyProtected);
            return SuggestionApiKey; // 레거시 폴백
        }
        set
        {
            SuggestionApiKeyProtected = string.IsNullOrEmpty(value) ? null : EncryptApiKey(value);
            SuggestionApiKey = null; // 레거시 필드 제거
        }
    }

    /// <summary>커스텀 base URL (고급 사용자용, null이면 프로바이더 기본값)</summary>
    public string? SuggestionApiBaseUrl { get; set; }

    /// <summary>모델 이름 (null이면 프로바이더 기본값)</summary>
    public string? SuggestionApiModel { get; set; }

    /// <summary>디버그 모드 — 페르소나 파이프라인 모니터링 창 활성화</summary>
    public bool DebugMode { get; set; }

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
            // 평문 레거시 키가 남아있으면 암호화로 마이그레이션
            if (!string.IsNullOrEmpty(SuggestionApiKey) && string.IsNullOrEmpty(SuggestionApiKeyProtected))
                SuggestionApiKeyDecrypted = SuggestionApiKey;

            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private static string EncryptApiKey(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string? DecryptApiKey(string base64)
    {
        try
        {
            var encrypted = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }
}
