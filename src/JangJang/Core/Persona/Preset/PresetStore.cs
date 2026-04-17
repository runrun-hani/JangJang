using System.IO;
using System.Reflection;
using System.Text.Json;

namespace JangJang.Core.Persona.Preset;

/// <summary>
/// 앱에 번들된 프리셋 JSON을 로드한다.
/// 프리셋 파일은 EmbeddedResource로 어셈블리에 포함된다.
/// 리소스 이름 규칙: JangJang.Resources.Presets.{id}.json
/// </summary>
public static class PresetStore
{
    private static readonly Assembly _assembly = typeof(PresetStore).Assembly;
    private static readonly string _resourcePrefix = "JangJang.Resources.Presets.";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// 모든 번들 프리셋을 로드한다.
    /// 로드 실패한 개별 프리셋은 조용히 스킵한다.
    /// </summary>
    public static List<PersonaPreset> LoadAll()
    {
        var presets = new List<PersonaPreset>();
        var resourceNames = _assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(_resourcePrefix) && n.EndsWith(".json"));

        foreach (var name in resourceNames)
        {
            var preset = LoadFromResource(name);
            if (preset != null)
                presets.Add(preset);
        }

        return presets;
    }

    /// <summary>
    /// ID로 특정 프리셋을 로드한다. 없으면 null.
    /// </summary>
    public static PersonaPreset? LoadById(string id)
    {
        var resourceName = _resourcePrefix + id + ".json";
        return LoadFromResource(resourceName);
    }

    private static PersonaPreset? LoadFromResource(string resourceName)
    {
        try
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;
            return JsonSerializer.Deserialize<PersonaPreset>(stream, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
