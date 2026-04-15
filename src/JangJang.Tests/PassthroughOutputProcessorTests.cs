using JangJang.Core.Persona;
using JangJang.Core.Persona.Pipeline;
using Xunit;

namespace JangJang.Tests;

public class PassthroughOutputProcessorTests
{
    [Fact]
    public void Process_EmptyCandidates_ReturnsEmptyString()
    {
        var proc = new PassthroughOutputProcessor();
        var result = proc.Process(new List<SeedLine>(), new DialogueContext());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Process_SingleCandidate_ReturnsThatText()
    {
        var proc = new PassthroughOutputProcessor();
        var seed = new SeedLine { Text = "유일한 대사" };

        var result = proc.Process(new List<SeedLine> { seed }, new DialogueContext());

        Assert.Equal("유일한 대사", result);
    }

    [Fact]
    public void Process_RepeatedCalls_ShowsVarietyAcrossCandidates()
    {
        var proc = new PassthroughOutputProcessor();
        var candidates = new List<SeedLine>
        {
            new() { Text = "대사 A" },
            new() { Text = "대사 B" },
            new() { Text = "대사 C" },
        };

        var seen = new HashSet<string>();
        for (int i = 0; i < 30; i++)
        {
            var r = proc.Process(candidates, new DialogueContext());
            seen.Add(r);
        }

        // 최근 N개 가중치 감소 로직이 작동하면 최소 2개 이상의 서로 다른 대사가 관찰돼야 한다.
        // 실패하면 동일 대사만 30번 나오는 결정론 버그이므로 회귀 감지로 유용.
        Assert.True(seen.Count >= 2, $"30번 실행 중 관찰된 서로 다른 대사: {seen.Count}개 — 반복 방지 로직이 작동하지 않음");
    }
}
