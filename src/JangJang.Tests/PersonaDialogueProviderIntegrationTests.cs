using System.IO;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Embedding;
using JangJang.Core.Persona.Pipeline;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// 페르소나 대사 파이프라인 종단 간 통합 테스트.
/// 모델 파일이 있을 때만 실행. 5개 샘플 씨앗으로 Provider를 조립하고,
/// GetLine이 씨앗 중 하나를 반환하는지 + "점심" 쿼리에 대해 "점심" 씨앗을 선호하는지 검증.
///
/// ActivityMonitor 의존성을 피하기 위해 PersonaDialogueProvider.Create() 헬퍼 대신
/// 수동 조립을 사용한다 (StubContextCollector로 ActivityMonitor 없이 파이프라인 구성).
/// </summary>
public class PersonaDialogueProviderIntegrationTests : IDisposable
{
    private readonly string _tempCacheDir;
    private readonly string _cachePath;

    public PersonaDialogueProviderIntegrationTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "JangJangTests_Persona_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempCacheDir);
        _cachePath = Path.Combine(_tempCacheDir, "embeddings.bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempCacheDir, true); } catch { }
    }

    private class IdentityContextCollector : IContextCollector
    {
        public DialogueContext Collect(DialogueContext basicContext) => basicContext;
    }

    private static PersonaData MakeSamplePersona() => new()
    {
        Name = "테스트 최애",
        PortraitFileName = "portrait.png",
        SeedLines = new List<SeedLine>
        {
            new() { Text = "점심 맛있게 먹어", SituationDescription = "점심 시간" },
            new() { Text = "야 일 해", SituationDescription = "작업 중 딴짓할 때" },
            new() { Text = "오늘도 화이팅", SituationDescription = "아침에 시작할 때" },
            new() { Text = "좀 쉬어도 돼", SituationDescription = "오래 집중한 뒤" },
            new() { Text = "잘자", SituationDescription = "밤 늦은 시간" },
        }
    };

    private PersonaDialogueProvider BuildProvider()
    {
        var embedder = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);
        var persona = MakeSamplePersona();
        var collector = new IdentityContextCollector();
        var selector = new EmbeddingCandidateSelector(embedder, persona, _cachePath);
        var processor = new PassthroughOutputProcessor();
        return new PersonaDialogueProvider(embedder, collector, selector, processor);
    }

    [SkippableFact]
    public void GetLine_ReturnsOneOfSeedTexts()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var provider = BuildProvider();

        var seedTexts = MakeSamplePersona().SeedLines.Select(s => s.Text).ToHashSet();
        var line = provider.GetLine(new DialogueContext
        {
            State = PetState.Happy,
            Timestamp = new DateTime(2026, 4, 16, 14, 0, 0)
        });

        Assert.NotEmpty(line);
        Assert.Contains(line, seedTexts);
    }

    [SkippableFact]
    public void GetLine_LunchTimeContext_PrefersLunchSeed()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var provider = BuildProvider();

        // 점심 무렵 상황. ContextNarrator가 "점심 무렵" 키워드를 포함한 서술을 만들 것.
        // PassthroughOutputProcessor에 가중치 랜덤이 있어 100% 확정은 어려우므로
        // 여러 번 호출해 "점심" 씨앗이 최소 1회는 선택되는지 확인 (느슨한 검증).
        var ctx = new DialogueContext
        {
            State = PetState.Happy,
            Timestamp = new DateTime(2026, 4, 16, 12, 30, 0) // 점심 무렵
        };

        var results = new List<string>();
        for (int i = 0; i < 10; i++)
            results.Add(provider.GetLine(ctx));

        Assert.Contains("점심 맛있게 먹어", results);
    }
}
