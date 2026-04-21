using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Pipeline;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// ContextNarrator는 상황을 한국어 자연어 문장으로 변환한다 — 임베딩 매칭의 "쿼리 문장".
/// 특정 컨텍스트 입력에 대해 예상 키워드가 포함되는지 스냅샷 테스트한다.
/// </summary>
public class ContextNarratorTests
{
    private readonly ContextNarrator _narrator = new();

    [Fact]
    public void Narrate_HappyEarlyMorning_ContainsFocusAndMorningKeywords()
    {
        var ctx = new DialogueContext
        {
            State = PetState.Happy,
            Timestamp = new DateTime(2026, 4, 16, 7, 30, 0) // 이른 아침
        };

        var text = _narrator.Narrate(ctx);

        Assert.Contains("집중", text);
        Assert.Contains("아침", text);
    }

    [Fact]
    public void Narrate_AnnoyedHighEvening_ContainsAnnoyedAndEveningKeywords()
    {
        var ctx = new DialogueContext
        {
            State = PetState.Annoyed,
            Annoyance = 0.8,
            Timestamp = new DateTime(2026, 4, 16, 19, 0, 0) // 저녁
        };

        var text = _narrator.Narrate(ctx);

        Assert.Contains("답답", text);
        Assert.Contains("저녁", text);
    }

    [Fact]
    public void Narrate_LongSession_MentionsHours()
    {
        var ctx = new DialogueContext
        {
            State = PetState.Happy,
            SessionSeconds = 4 * 3600, // 4시간
            Timestamp = new DateTime(2026, 4, 16, 14, 0, 0)
        };

        var text = _narrator.Narrate(ctx);

        Assert.Contains("4시간", text);
    }
}
