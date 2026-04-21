using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JangJang.Core.Persona;

/// <summary>
/// 다중 페르소나 저장소. 각 페르소나는 자체 GUID 폴더를 갖는다.
/// 저장 위치:
///   %AppData%/JangJang/Personas/{personaId}/
///     ├── persona.json
///     ├── portrait.{ext}
///     ├── embeddings.bin
///     └── feedback.json
///
/// "삭제" 개념은 이 레이어에 없다. AppSettings.RegisteredPersonaIds에서 Id를 빼는 것이
/// 앱 입장의 "제거"이며, 디스크 파일은 보존된다 (사용자가 실수로 만든 흔적이 사라지지 않도록).
/// </summary>
public static class PersonaStore
{
    public static string PersonasRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JangJang",
            "Personas");

    /// <summary>[레거시] 기존 단일 페르소나 폴더. 마이그레이션 대상.</summary>
    public static string LegacyCurrentDir { get; } = Path.Combine(PersonasRoot, "current");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>특정 페르소나의 폴더 경로.</summary>
    public static string GetPersonaDir(string personaId) => Path.Combine(PersonasRoot, personaId);

    public static string GetPersonaJsonPath(string personaId) =>
        Path.Combine(GetPersonaDir(personaId), "persona.json");

    public static string GetEmbeddingsCachePath(string personaId) =>
        Path.Combine(GetPersonaDir(personaId), "embeddings.bin");

    public static string GetFeedbackJsonPath(string personaId) =>
        Path.Combine(GetPersonaDir(personaId), "feedback.json");

    /// <summary>해당 Id의 persona.json이 존재하는지.</summary>
    public static bool Exists(string personaId) =>
        !string.IsNullOrEmpty(personaId) && File.Exists(GetPersonaJsonPath(personaId));

    /// <summary>
    /// 초상화 파일의 전체 경로. PortraitFileName이 비어 있으면 기본 "portrait.png".
    /// </summary>
    public static string GetPortraitFullPath(PersonaData data)
    {
        var fileName = string.IsNullOrWhiteSpace(data.PortraitFileName)
            ? "portrait.png"
            : data.PortraitFileName;
        return Path.Combine(GetPersonaDir(data.Id), fileName);
    }

    /// <summary>
    /// 지정된 Id의 페르소나 로드. 파일이 없거나 파싱 실패 시 null.
    /// 로드된 데이터의 Id가 비어 있으면 파라미터의 personaId를 채워 넣는다(하위 호환).
    /// </summary>
    public static PersonaData? Load(string personaId)
    {
        if (string.IsNullOrEmpty(personaId)) return null;
        try
        {
            var path = GetPersonaJsonPath(personaId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<PersonaData>(json, _jsonOptions);
            if (data == null) return null;
            if (string.IsNullOrEmpty(data.Id))
                data.Id = personaId;
            MigrateIfNeeded(data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 페르소나 저장. data.Id가 비어 있으면 새 GUID 발급. 해당 폴더가 없으면 생성.
    /// </summary>
    public static void Save(PersonaData data)
    {
        try
        {
            if (string.IsNullOrEmpty(data.Id))
                data.Id = Guid.NewGuid().ToString("N");
            var dir = GetPersonaDir(data.Id);
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(GetPersonaJsonPath(data.Id), json);
        }
        catch { }
    }

    /// <summary>
    /// 외부 폴더를 페르소나로 불러오기. 지정 경로에 persona.json이 있어야 한다.
    /// 내부 폴더에 복사하고 새 Id를 부여해 저장. 반환값은 새 페르소나 Id.
    /// 초상화 파일이 같은 폴더에 있으면 함께 복사한다.
    /// 실패 시 null.
    /// </summary>
    public static string? ImportFromFolder(string sourceFolder)
    {
        try
        {
            var sourceJson = Path.Combine(sourceFolder, "persona.json");
            if (!File.Exists(sourceJson)) return null;

            var json = File.ReadAllText(sourceJson);
            var data = JsonSerializer.Deserialize<PersonaData>(json, _jsonOptions);
            if (data == null) return null;

            data.Id = Guid.NewGuid().ToString("N");
            var destDir = GetPersonaDir(data.Id);
            Directory.CreateDirectory(destDir);

            if (!string.IsNullOrEmpty(data.PortraitFileName))
            {
                // 상대 경로에 ".."·절대경로가 섞여 있을 수 있으므로 파일명만 남겨 경로 이탈을 차단.
                var safeName = Path.GetFileName(data.PortraitFileName);
                if (!string.IsNullOrEmpty(safeName))
                {
                    data.PortraitFileName = safeName;
                    var sourcePortrait = Path.Combine(sourceFolder, safeName);
                    if (File.Exists(sourcePortrait))
                    {
                        var destPortrait = Path.Combine(destDir, safeName);
                        File.Copy(sourcePortrait, destPortrait, overwrite: true);
                    }
                }
            }

            MigrateIfNeeded(data);
            var outJson = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(GetPersonaJsonPath(data.Id), outJson);
            return data.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 레거시 current/ 폴더를 GUID 폴더로 이전한다.
    /// - current/persona.json이 있고 GUID 폴더로 아직 옮기지 않았을 때만 동작
    /// - 반환값: 새 페르소나 Id (마이그레이션 실행됨) / null (대상 없음 or 실패)
    /// - 호출자는 AppSettings.RegisteredPersonaIds / ActivePersonaId를 갱신해야 한다.
    /// </summary>
    public static string? MigrateLegacyCurrent()
    {
        try
        {
            var legacyJson = Path.Combine(LegacyCurrentDir, "persona.json");
            if (!File.Exists(legacyJson)) return null;

            // Id를 결정해서 폴더 이름을 바꾼다 (복사 대신 rename으로 데이터 보존 + 속도)
            var json = File.ReadAllText(legacyJson);
            var data = JsonSerializer.Deserialize<PersonaData>(json, _jsonOptions);
            if (data == null) return null;

            var newId = string.IsNullOrEmpty(data.Id) ? Guid.NewGuid().ToString("N") : data.Id;
            data.Id = newId;

            var destDir = GetPersonaDir(newId);
            if (Directory.Exists(destDir))
                return null; // 이미 같은 Id 폴더 존재 — 중복 마이그레이션 방지

            Directory.Move(LegacyCurrentDir, destDir);

            // Id를 persist
            MigrateIfNeeded(data);
            var outJson = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(GetPersonaJsonPath(newId), outJson);
            return newId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Personas 루트 아래에 있는 모든 실제 페르소나 폴더의 Id 나열.
    /// "불러오기" UI가 후보를 보여줄 때 사용 (이미 등록된 Id 제외는 호출자 책임).
    /// </summary>
    public static IEnumerable<string> EnumerateAllPersonaIdsOnDisk()
    {
        if (!Directory.Exists(PersonasRoot)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(PersonasRoot))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, "current", StringComparison.OrdinalIgnoreCase))
                continue; // 레거시는 제외
            var json = Path.Combine(dir, "persona.json");
            if (File.Exists(json))
                yield return name;
        }
    }

    /// <summary>기존 persona.json에서 새 필드 보정.</summary>
    private static void MigrateIfNeeded(PersonaData data)
    {
        if (string.IsNullOrEmpty(data.CustomToneDescription) && !string.IsNullOrEmpty(data.ToneHint))
            data.CustomToneDescription = data.ToneHint;
    }

    /// <summary>
    /// 선택된 초상화 파일을 해당 페르소나 폴더로 복사한다.
    /// 반환값은 persona.json에 저장할 상대 파일명.
    /// 예외는 호출자에게 전파 — UI에서 오류 메시지 표시.
    /// </summary>
    public static string CopyPortrait(string personaId, string sourcePath)
    {
        if (string.IsNullOrEmpty(personaId))
            throw new ArgumentException("personaId required", nameof(personaId));
        var dir = GetPersonaDir(personaId);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        var fileName = "portrait" + ext;
        var destPath = Path.Combine(dir, fileName);
        File.Copy(sourcePath, destPath, overwrite: true);
        return fileName;
    }
}
