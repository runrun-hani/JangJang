using JangJang.Core.Persona.Embedding;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// OnnxEmbeddingService의 정적 메서드(CosineSimilarity)만 테스트한다.
/// 인스턴스 메서드(EmbedQuery, EmbedPassage)는 실제 ONNX 모델이 필요해
/// Tier 2 통합 테스트에서 다룬다.
/// </summary>
public class OnnxEmbeddingServiceTests
{
    [Fact]
    public void CosineSimilarity_IdenticalUnitVectors_ReturnsOne()
    {
        // (0.6, 0.8) — L2 norm = 1.0
        var a = new[] { 0.6f, 0.8f };
        var b = new[] { 0.6f, 0.8f };
        var sim = OnnxEmbeddingService.CosineSimilarity(a, b);
        Assert.Equal(1.0f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeUnitVectors_ReturnsNegativeOne()
    {
        var a = new[] { 0.6f, 0.8f };
        var b = new[] { -0.6f, -0.8f };
        var sim = OnnxEmbeddingService.CosineSimilarity(a, b);
        Assert.Equal(-1.0f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalUnitVectors_ReturnsZero()
    {
        var a = new[] { 1.0f, 0.0f, 0.0f };
        var b = new[] { 0.0f, 1.0f, 0.0f };
        var sim = OnnxEmbeddingService.CosineSimilarity(a, b);
        Assert.Equal(0.0f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_LengthMismatch_ThrowsArgumentException()
    {
        var a = new[] { 1.0f, 0.0f };
        var b = new[] { 0.0f, 1.0f, 0.0f };
        Assert.Throws<ArgumentException>(() => OnnxEmbeddingService.CosineSimilarity(a, b));
    }
}
