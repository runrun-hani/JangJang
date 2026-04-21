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

    /// <summary>
    /// 대사가 자동으로 교체되는 주기(초). 상태가 바뀌는 순간에는 이 값과 무관하게 즉시 새 대사가 나온다.
    /// 유효 범위: 5 ~ 120초. 잘못된 값은 DialogueIntervalSecondsClamped에서 보정한다.
    /// </summary>
    public int DialogueIntervalSeconds { get; set; } = 25;

    /// <summary>범위 밖 값을 안전한 범위로 보정한 실제 사용 값.</summary>
    [JsonIgnore]
    public int DialogueIntervalSecondsClamped =>
        Math.Clamp(DialogueIntervalSeconds, 5, 120);
    public string TargetProcessName { get; set; } = "CLIPStudioPaint";
    public string TargetDisplayName { get; set; } = "Clip Studio Paint";

    /// <summary>
    /// 자캐 페르소나 모드 활성화 여부. true이면 페르소나의 작성 대사 풀이 사용된다.
    /// 매칭 방식은 EmbeddingMatchingEnabled로 결정된다:
    ///   - false(또는 모델 미설치): PersonaRandomDialogueProvider — 상태별 랜덤
    ///   - true + 모델 설치됨: PersonaDialogueProvider — 임베딩 파이프라인
    /// </summary>
    public bool PersonaEnabled { get; set; }

    /// <summary>
    /// 임베딩 기반 매칭 사용 여부. 기본값 false — 모델 다운로드 없이도 페르소나 모드를 즉시 쓸 수 있게.
    /// true여도 모델이 없으면 자동으로 랜덤 매칭으로 폴백.
    /// 설정 변경은 앱 재시작 후 반영된다 (Provider는 스타트업에서 1회 결정).
    /// </summary>
    public bool EmbeddingMatchingEnabled { get; set; }

    /// <summary>
    /// 설정에 등록된 페르소나 Id 화이트리스트. 폴더가 실제로 존재해도 여기에 없으면 앱에서 보이지 않는다.
    /// "삭제" 행위는 이 목록에서 제거할 뿐 실제 파일은 건드리지 않는다.
    /// </summary>
    public List<string> RegisteredPersonaIds { get; set; } = new();

    /// <summary>
    /// 현재 활성 페르소나 Id. null이면 활성 페르소나 없음 → 페르소나 Provider 비활성.
    /// RegisteredPersonaIds에 포함된 값이어야 유효하다.
    /// </summary>
    public string? ActivePersonaId { get; set; }

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
