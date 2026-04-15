using JangJang.Core.Persona;

namespace JangJang.Core;

/// <summary>
/// 대사 진입점. 내부적으로 현재 활성 IDialogueProvider에 위임한다.
/// 기존 호출 시그니처(GetLine(state, annoyance, todaySeconds))는 그대로 유지하여
/// PetViewModel 등 호출 측은 변경하지 않는다.
/// </summary>
public static class Dialogue
{
    private static IDialogueProvider _current = new DefaultDialogueProvider();

    /// <summary>
    /// 현재 활성 Provider를 교체한다.
    /// 앱 시작 시 AppSettings에 따라 DefaultDialogueProvider 또는 PersonaDialogueProvider로 설정한다.
    /// </summary>
    public static void SetProvider(IDialogueProvider provider) => _current = provider;

    public static string GetLine(PetState state, double annoyance, int todaySeconds)
    {
        var ctx = new DialogueContext
        {
            State = state,
            Annoyance = annoyance,
            TodaySeconds = todaySeconds
        };
        return _current.GetLine(ctx);
    }
}
