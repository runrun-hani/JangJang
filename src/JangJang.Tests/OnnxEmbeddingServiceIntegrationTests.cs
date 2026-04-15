using JangJang.Core.Persona.Embedding;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// 실제 ONNX 모델이 있을 때만 실행되는 통합 테스트.
/// 모델이 없으면 Skip.If로 건너뜀 (실패 아님).
///
/// 표준 위치(%AppData%/JangJang/Models/multilingual-e5-small/)에 모델 파일과
/// 토크나이저 파일이 모두 있어야 실행.
///
/// tools/fetch-test-model.ps1 또는 자캐 모드 첫 실행을 통해 모델을 배치한 뒤 실행 가능.
/// </summary>
public class OnnxEmbeddingServiceIntegrationTests
{
    [SkippableFact]
    public void LoadAndEmbedQuery_ReturnsNormalizedVector()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);
        var vec = service.EmbedQuery("오늘 작업 시작할 때");

        Assert.NotNull(vec);
        Assert.Equal(service.Dimension, vec.Length);

        // L2 norm ≈ 1 (정규화 확인)
        double normSquared = 0;
        foreach (var v in vec) normSquared += v * v;
        var norm = Math.Sqrt(normSquared);
        Assert.InRange(norm, 0.99, 1.01);
    }

    [SkippableFact]
    public void EmbedPassage_ReturnsNormalizedVector()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);
        var vec = service.EmbedPassage("야 뭐 해");

        Assert.NotNull(vec);
        Assert.Equal(service.Dimension, vec.Length);

        double normSquared = 0;
        foreach (var v in vec) normSquared += v * v;
        var norm = Math.Sqrt(normSquared);
        Assert.InRange(norm, 0.99, 1.01);
    }

    [SkippableFact]
    public void Cosine_SimilarSentences_HigherThanUnrelatedSentences()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        // 비슷한 의미
        var query = service.EmbedQuery("오래 앉아서 지쳐 보인다");
        var related = service.EmbedPassage("힘들지? 잠깐 쉬어");

        // 전혀 무관한 의미
        var unrelated = service.EmbedPassage("고양이는 귀엽다");

        var simRelated = OnnxEmbeddingService.CosineSimilarity(query, related);
        var simUnrelated = OnnxEmbeddingService.CosineSimilarity(query, unrelated);

        // 의미 유사한 쪽이 무관한 쪽보다 더 높은 유사도를 가져야 한다.
        // 느슨한 검증 — 매칭 품질의 방향성만 확인.
        Assert.True(simRelated > simUnrelated,
            $"유사 문장 코사인({simRelated:F3})이 무관 문장({simUnrelated:F3})보다 작음 — 매칭 품질 의심");
    }
}
