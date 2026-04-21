using JangJang.Core;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// PersonaRandomDialogueProvider는 페르소나 작성 대사 풀에서 PetState로 필터링 후
/// 가중치 랜덤으로 한 줄을 뽑는다. 해당 상태 대사가 0개면 빈 문자열 반환 (상위 Dialogue.cs가 폴백).
/// </summary>
public class PersonaRandomDialogueProviderTests
{
    private static PersonaData BuildPersona(params (string text, PetState state)[] lines)
    {
        var persona = new PersonaData { Id = "test", Name = "테스트" };
        foreach (var (text, state) in lines)
        {
            persona.SeedLines.Add(new SeedLine { Text = text, State = state });
        }
        return persona;
    }

    [Fact]
    public void GetLine_MatchingState_ReturnsLineFromThatStateOnly()
    {
        var persona = BuildPersona(
            ("행복 대사 1", PetState.Happy),
            ("행복 대사 2", PetState.Happy),
            ("경고 대사", PetState.Alert));
        var provider = new PersonaRandomDialogueProvider(persona);

        // 여러 번 호출해도 항상 Happy 대사만 나와야 한다 (상태 필터링 검증).
        var happyLines = new[] { "행복 대사 1", "행복 대사 2" };
        for (int i = 0; i < 20; i++)
        {
            var line = provider.GetLine(new DialogueContext { State = PetState.Happy });
            Assert.Contains(line, happyLines);
        }
    }

    [Fact]
    public void GetLine_SingleCandidate_ReturnsThatText()
    {
        var persona = BuildPersona(("유일한 대사", PetState.Sleeping));
        var provider = new PersonaRandomDialogueProvider(persona);

        var line = provider.GetLine(new DialogueContext { State = PetState.Sleeping });

        Assert.Equal("유일한 대사", line);
    }

    [Fact]
    public void GetLine_EmptyPersona_ReturnsEmptyString()
    {
        var persona = new PersonaData { Id = "empty", Name = "빈 페르소나" };
        var provider = new PersonaRandomDialogueProvider(persona);

        var line = provider.GetLine(new DialogueContext { State = PetState.Happy });

        Assert.Equal(string.Empty, line);
    }

    [Fact]
    public void GetLine_NoMatchingState_ReturnsEmptyString()
    {
        // Alert 상태 대사만 있는데 Happy를 요청하면 빈 문자열 → Dialogue.cs 폴백이 동작.
        var persona = BuildPersona(("경고!", PetState.Alert));
        var provider = new PersonaRandomDialogueProvider(persona);

        var line = provider.GetLine(new DialogueContext { State = PetState.Happy });

        Assert.Equal(string.Empty, line);
    }

    [Fact]
    public void GetLine_RepeatedCalls_ShowsVariety()
    {
        var persona = BuildPersona(
            ("대사 A", PetState.Happy),
            ("대사 B", PetState.Happy),
            ("대사 C", PetState.Happy));
        var provider = new PersonaRandomDialogueProvider(persona);

        var seen = new HashSet<string>();
        for (int i = 0; i < 30; i++)
        {
            var line = provider.GetLine(new DialogueContext { State = PetState.Happy });
            seen.Add(line);
        }

        // PassthroughOutputProcessor의 반복 방지 가중치가 작동하면 최소 2종 이상 관찰.
        Assert.True(seen.Count >= 2,
            $"30번 중 관찰된 서로 다른 대사: {seen.Count}개 — 반복 방지 로직이 작동하지 않음");
    }
}
