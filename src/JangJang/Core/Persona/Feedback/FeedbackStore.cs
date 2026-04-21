using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JangJang.Core.Persona.Feedback;

/// <summary>
/// 피드백을 feedback.json에 누적 저장한다. 페르소나별로 독립적.
/// 최근 100건만 유지하여 파일 크기를 제한한다.
/// PersonaStore와 동일한 직렬화 패턴 (System.Text.Json, 조용한 실패).
/// </summary>
public static class FeedbackStore
{
    private const int MaxEntries = 100;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>피드백 목록 로드. personaId가 비었거나 파일이 없으면 빈 리스트.</summary>
    public static List<DialogueFeedback> Load(string? personaId)
    {
        if (string.IsNullOrEmpty(personaId)) return new();
        try
        {
            var path = PersonaStore.GetFeedbackJsonPath(personaId);
            if (!File.Exists(path))
                return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<DialogueFeedback>>(json, _jsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>피드백 목록 저장. 100건 초과 시 오래된 것부터 잘라낸다.</summary>
    public static void Save(string? personaId, List<DialogueFeedback> feedbacks)
    {
        if (string.IsNullOrEmpty(personaId)) return;
        try
        {
            var trimmed = feedbacks.Count > MaxEntries
                ? feedbacks.Skip(feedbacks.Count - MaxEntries).ToList()
                : feedbacks;

            Directory.CreateDirectory(PersonaStore.GetPersonaDir(personaId));
            var json = JsonSerializer.Serialize(trimmed, _jsonOptions);
            File.WriteAllText(PersonaStore.GetFeedbackJsonPath(personaId), json);
        }
        catch { }
    }

    /// <summary>피드백 한 건 추가 후 저장.</summary>
    public static void Append(string? personaId, DialogueFeedback feedback)
    {
        if (string.IsNullOrEmpty(personaId)) return;
        var list = Load(personaId);
        list.Add(feedback);
        Save(personaId, list);
    }
}
