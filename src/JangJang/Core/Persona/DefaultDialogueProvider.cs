namespace JangJang.Core.Persona;

/// <summary>
/// 기존 치와와 하드코딩 대사 Provider.
/// Dialogue.cs의 정적 배열 로직을 그대로 이관한다 (회귀 0 목표).
/// Persona 모드가 꺼져 있을 때 또는 PersonaDialogueProvider 초기화 실패 시 폴백으로 사용된다.
/// </summary>
public sealed class DefaultDialogueProvider : IDialogueProvider
{
    private static readonly Random Rand = new();

    private static readonly string[] HappyLines =
    { "열심히 일하는 중!", "집중!", "좋아", "굿", "이 느낌", "오" };

    private static readonly string[] AlertLines =
    { "...뭐 하는 거야?", "어디 갔어?", "야", "딴 짓 하지?", "돌아와" };

    private static readonly string[] AnnoyedMildLines =
    { "일 안 해?!", "딴짓?", "야!!", "마감 언제인데" };

    private static readonly string[] AnnoyedFuriousLines =
    { "당장 일해!!!", "마감이잖아", "!!!", "정신 차려!!!" };

    private static readonly string[] SleepingLines =
    { "zzZ", "...", "(쿨쿨)" };

    private static readonly string[] WakeUpLines =
    { "..." };

    private static string Pick(string[] lines) => lines[Rand.Next(lines.Length)];

    public string GetLine(DialogueContext context)
    {
        return context.State switch
        {
            PetState.Happy => Pick(HappyLines),
            PetState.Alert => Pick(AlertLines),
            PetState.Annoyed => context.Annoyance < 0.5 ? Pick(AnnoyedMildLines) : Pick(AnnoyedFuriousLines),
            PetState.Sleeping => Pick(SleepingLines),
            PetState.WakeUp => Pick(WakeUpLines),
            _ => "..."
        };
    }
}
