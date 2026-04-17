using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JangJang.Core.Persona;

/// <summary>
/// 현재 활성 페르소나 데이터를 로드/저장한다.
/// 저장 위치:
///   %AppData%/JangJang/Personas/current/
///     ├── persona.json
///     ├── portrait.{ext}
///     └── embeddings.bin   (Step 3에서 사용)
///
/// AppSettings와 동일한 직렬화 패턴 (System.Text.Json, WriteIndented, 조용한 실패)을 따른다.
/// </summary>
public static class PersonaStore
{
    public static string PersonasRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JangJang",
            "Personas");

    public static string CurrentDir { get; } = Path.Combine(PersonasRoot, "current");

    public static string PersonaJsonPath { get; } = Path.Combine(CurrentDir, "persona.json");

    public static string EmbeddingsCachePath { get; } = Path.Combine(CurrentDir, "embeddings.bin");

    public static string FeedbackJsonPath { get; } = Path.Combine(CurrentDir, "feedback.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>현재 페르소나 폴더에 persona.json이 존재하는지.</summary>
    public static bool Exists() => File.Exists(PersonaJsonPath);

    /// <summary>
    /// 초상화 파일의 전체 경로를 돌려준다.
    /// PortraitFileName이 비었거나 null이면 기본 "portrait.png" 사용.
    /// </summary>
    public static string GetPortraitFullPath(PersonaData data)
    {
        var fileName = string.IsNullOrWhiteSpace(data.PortraitFileName)
            ? "portrait.png"
            : data.PortraitFileName;
        return Path.Combine(CurrentDir, fileName);
    }

    /// <summary>
    /// 현재 페르소나 로드. 파일이 없거나 파싱 실패 시 null 반환.
    /// 호출자는 null일 때 "페르소나 미설정" 상태로 처리해야 한다.
    /// </summary>
    public static PersonaData? Load()
    {
        try
        {
            if (!File.Exists(PersonaJsonPath))
                return null;
            var json = File.ReadAllText(PersonaJsonPath);
            var data = JsonSerializer.Deserialize<PersonaData>(json, _jsonOptions);
            if (data != null)
                MigrateIfNeeded(data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 현재 페르소나 저장. 디렉토리가 없으면 생성한다.
    /// </summary>
    public static void Save(PersonaData data)
    {
        try
        {
            Directory.CreateDirectory(CurrentDir);
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(PersonaJsonPath, json);
        }
        catch { }
    }

    /// <summary>
    /// 기존 persona.json에서 새 필드가 없는 경우 마이그레이션한다.
    /// ToneHint → CustomToneDescription 복사 (새 필드가 비어있을 때만).
    /// </summary>
    private static void MigrateIfNeeded(PersonaData data)
    {
        if (string.IsNullOrEmpty(data.CustomToneDescription) && !string.IsNullOrEmpty(data.ToneHint))
            data.CustomToneDescription = data.ToneHint;
    }

    /// <summary>
    /// 사용자가 선택한 초상화 파일을 current 폴더로 복사한다.
    /// 반환값은 persona.json에 저장할 상대 파일명 (예: "portrait.png").
    /// 예외는 호출자에게 전파 — UI에서 오류 메시지를 표시할 수 있도록 함.
    /// </summary>
    public static string CopyPortrait(string sourcePath)
    {
        Directory.CreateDirectory(CurrentDir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        var fileName = "portrait" + ext;
        var destPath = Path.Combine(CurrentDir, fileName);
        File.Copy(sourcePath, destPath, overwrite: true);
        return fileName;
    }
}
