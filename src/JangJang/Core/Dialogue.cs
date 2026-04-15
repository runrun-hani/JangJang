using JangJang.Core.Persona;

namespace JangJang.Core;

/// <summary>
/// 대사 진입점. 내부적으로 현재 활성 IDialogueProvider에 위임한다.
/// 기존 호출 시그니처(GetLine(state, annoyance, todaySeconds))는 그대로 유지하여
/// PetViewModel 등 호출 측은 변경하지 않는다.
/// </summary>
public static class Dialogue
{
    private static readonly DefaultDialogueProvider _default = new();
    private static IDialogueProvider _current = _default;

    /// <summary>
    /// 현재 활성 Provider를 교체한다.
    /// 앱 시작 시 AppSettings에 따라 DefaultDialogueProvider 또는 PersonaDialogueProvider로 설정한다.
    /// null을 넘기면 기본 Provider로 리셋.
    /// </summary>
    public static void SetProvider(IDialogueProvider? provider) => _current = provider ?? _default;

    /// <summary>현재 Provider를 기본(치와와)으로 강제 리셋.</summary>
    public static void ResetToDefault() => _current = _default;

    public static string GetLine(PetState state, double annoyance, int todaySeconds)
    {
        var ctx = new DialogueContext
        {
            State = state,
            Annoyance = annoyance,
            TodaySeconds = todaySeconds
        };
        var line = _current.GetLine(ctx);

        // 폴백: PersonaDialogueProvider가 빈 문자열 반환 시(파이프라인 실패/후보 0)
        // 기본 Provider로 즉시 대체. 사용자에겐 자캐 대신 치와와 대사가 잠깐 보임 — 무음보다는 안전.
        if (string.IsNullOrEmpty(line) && !ReferenceEquals(_current, _default))
            line = _default.GetLine(ctx);

        return line;
    }
}
