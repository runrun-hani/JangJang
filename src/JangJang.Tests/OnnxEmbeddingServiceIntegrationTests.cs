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

    /// <summary>
    /// Raw e5 임베딩은 anisotropy로 인해 무관 문장 간에도 cosine이 0.85-0.92 영역에 몰린다.
    /// (2026-04-16 진단: 10개 무관 문장 all-pairs 평균 0.8867, stddev 0.0153)
    /// 직접 cosine 비교로 "관련 > 무관"을 strict하게 검증하는 것은 e5 모델 특성상 불안정.
    /// 이 테스트는 raw 임베딩이 최소한 붕괴(평균 >0.99, stddev <0.005)하지 않았다는
    /// sanity check만 수행. 실제 rank 품질은 EmbeddingCandidateSelector(centering 적용)를
    /// 거친 `Centered_Cosine_SimilarSentences_HigherThanUnrelated` 테스트에서 검증.
    /// </summary>
    [SkippableFact]
    public void Cosine_RawEmbeddingSanityCheck_NotCollapsed()
    {
        Skip.IfNot(ModelFolderLocator.IsAvailable(), ModelFolderLocator.MissingReason());

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        var sentences = new[]
        {
            "오래 앉아서 지쳐 보인다",
            "힘들지? 잠깐 쉬어",
            "고양이는 귀엽다",
            "프로그래밍은 재미있다",
            "오늘 날씨가 좋다"
        };

        var vectors = sentences.Select(s => service.EmbedPassage(s)).ToList();

        // all-pairs cosine
        var sims = new List<float>();
        for (int i = 0; i < vectors.Count; i++)
            for (int j = i + 1; j < vectors.Count; j++)
                sims.Add(OnnxEmbeddingService.CosineSimilarity(vectors[i], vectors[j]));

        var mean = sims.Average();
        var stdDev = Math.Sqrt(sims.Select(s => (s - mean) * (s - mean)).Average());

        // Sanity check: 완전 붕괴(모든 벡터가 identical) 또는 제로 분산이 아니어야 함
        Assert.True(mean < 0.99, $"cosine 평균이 너무 높음 ({mean:F4}) — 임베딩 붕괴 의심");
        Assert.True(stdDev > 0.005, $"cosine stddev가 너무 낮음 ({stdDev:F4}) — 변별력 0");

        // 각 벡터가 L2 정규화된 unit vector인지
        foreach (var v in vectors)
        {
            double n = 0;
            foreach (var x in v) n += x * x;
            Assert.InRange(Math.Sqrt(n), 0.99, 1.01);
        }
    }
}
