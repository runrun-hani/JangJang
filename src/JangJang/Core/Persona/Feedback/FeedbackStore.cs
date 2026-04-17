using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JangJang.Core.Persona.Feedback;

/// <summary>
/// 피드백을 feedback.json에 누적 저장한다.
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

    /// <summary>
    /// 피드백 목록 로드. 파일이 없거나 실패 시 빈 리스트.
    /// </summary>
    public static List<DialogueFeedback> Load()
    {
        try
        {
            var path = PersonaStore.FeedbackJsonPath;
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

    /// <summary>
    /// 피드백 목록 저장. 100건 초과 시 오래된 것부터 잘라낸다.
    /// </summary>
    public static void Save(List<DialogueFeedback> feedbacks)
    {
        try
        {
            var trimmed = feedbacks.Count > MaxEntries
                ? feedbacks.Skip(feedbacks.Count - MaxEntries).ToList()
                : feedbacks;

            Directory.CreateDirectory(PersonaStore.CurrentDir);
            var json = JsonSerializer.Serialize(trimmed, _jsonOptions);
            File.WriteAllText(PersonaStore.FeedbackJsonPath, json);
        }
        catch { }
    }

    /// <summary>
    /// 피드백 한 건을 추가하고 저장한다.
    /// </summary>
    public static void Append(DialogueFeedback feedback)
    {
        var list = Load();
        list.Add(feedback);
        Save(list);
    }
}
