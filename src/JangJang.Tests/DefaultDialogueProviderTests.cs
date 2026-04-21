using JangJang.Core;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// DefaultDialogueProvider는 기존 치와와 대사 로직을 이관한 것.
/// 각 상태 호출 시 해당 배열의 원소 중 하나가 반환되는지 확인한다.
/// </summary>
public class DefaultDialogueProviderTests
{
    private readonly DefaultDialogueProvider _provider = new();

    // 원본 Dialogue.cs의 배열과 동일해야 한다 (회귀 검증).
    private static readonly string[] HappyLines =
        { "열심히 일하는 중!", "집중!", "좋아", "굿", "이 느낌", "오" };
    private static readonly string[] AlertLines =
        { "...뭐 하는 거야?", "어디 갔어?", "야", "딴 짓 하지?", "돌아와" };
    private static readonly string[] AnnoyedMildLines =
        { "일 안 해?!", "딴짓?", "야!!", "마감 언제인데" };
    private static readonly string[] AnnoyedFuriousLines =
        { "당장 일해!!!", "마감이잖아", "!!!", "정신 차려!!!" };
    private static readonly string[] SleepingLines = { "zzZ", "...", "(쿨쿨)" };
    private static readonly string[] WakeUpLines = { "..." };

    [Fact]
    public void Happy_ReturnsHappyLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.Happy });
        Assert.Contains(line, HappyLines);
    }

    [Fact]
    public void Alert_ReturnsAlertLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.Alert });
        Assert.Contains(line, AlertLines);
    }

    [Fact]
    public void AnnoyedMild_AnnoyanceUnder05_ReturnsMildLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.Annoyed, Annoyance = 0.3 });
        Assert.Contains(line, AnnoyedMildLines);
    }

    [Fact]
    public void AnnoyedFurious_Annoyance05OrAbove_ReturnsFuriousLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.Annoyed, Annoyance = 0.7 });
        Assert.Contains(line, AnnoyedFuriousLines);
    }

    [Fact]
    public void Sleeping_ReturnsSleepingLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.Sleeping });
        Assert.Contains(line, SleepingLines);
    }

    [Fact]
    public void WakeUp_ReturnsWakeUpLine()
    {
        var line = _provider.GetLine(new DialogueContext { State = PetState.WakeUp });
        Assert.Contains(line, WakeUpLines);
    }
}
