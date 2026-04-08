namespace JangJang.Core;

public static class Dialogue
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

    public static string GetLine(PetState state, double annoyance, int todaySeconds)
    {
        return state switch
        {
            PetState.Happy => Pick(HappyLines),
            PetState.Alert => Pick(AlertLines),
            PetState.Annoyed => annoyance < 0.5 ? Pick(AnnoyedMildLines) : Pick(AnnoyedFuriousLines),
            PetState.Sleeping => Pick(SleepingLines),
            PetState.WakeUp => Pick(WakeUpLines),
            _ => "..."
        };
    }
}
