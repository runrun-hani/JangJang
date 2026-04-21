using System.IO;
using JangJang.Core.Persona.Embedding;
using Xunit;

namespace JangJang.Tests;

public class EmbeddingCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public EmbeddingCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JangJangTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cachePath = Path.Combine(_tempDir, "embeddings.bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ComputeTextHash_SameText_GivesSameHash()
    {
        var h1 = EmbeddingCache.ComputeTextHash("안녕 최애야");
        var h2 = EmbeddingCache.ComputeTextHash("안녕 최애야");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeTextHash_DifferentText_GivesDifferentHash()
    {
        var h1 = EmbeddingCache.ComputeTextHash("안녕");
        var h2 = EmbeddingCache.ComputeTextHash("잘가");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyDictionary()
    {
        var missing = Path.Combine(_tempDir, "nope.bin");
        var result = EmbeddingCache.Load(missing, 384);
        Assert.Empty(result);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllVectors()
    {
        const int dim = 4;
        var original = new Dictionary<long, float[]>
        {
            [1001L] = new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            [2002L] = new[] { 0.5f, 0.6f, 0.7f, 0.8f },
            [3003L] = new[] { -0.1f, -0.2f, -0.3f, -0.4f },
        };

        EmbeddingCache.Save(_cachePath, dim, original);
        var loaded = EmbeddingCache.Load(_cachePath, dim);

        Assert.Equal(3, loaded.Count);
        Assert.Equal(original[1001L], loaded[1001L]);
        Assert.Equal(original[2002L], loaded[2002L]);
        Assert.Equal(original[3003L], loaded[3003L]);
    }

    [Fact]
    public void Load_DimensionMismatch_ReturnsEmpty()
    {
        var dict = new Dictionary<long, float[]> { [1L] = new float[] { 0.1f, 0.2f } };
        EmbeddingCache.Save(_cachePath, 2, dict);

        // 다른 차원으로 로드 시도 → 안전하게 빈 딕셔너리
        var loaded = EmbeddingCache.Load(_cachePath, 384);
        Assert.Empty(loaded);
    }

    [Fact]
    public void Load_CorruptedMagicHeader_ReturnsEmpty()
    {
        // 헤더 4바이트가 유효한 magic이 아닌 파일
        File.WriteAllBytes(_cachePath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0 });
        var loaded = EmbeddingCache.Load(_cachePath, 4);
        Assert.Empty(loaded);
    }
}
