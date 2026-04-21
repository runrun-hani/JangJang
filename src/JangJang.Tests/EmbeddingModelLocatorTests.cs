using System.IO;
using JangJang.Core.Persona.Embedding;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// EmbeddingModelLocator의 파일 존재 판정 로직을 임시 폴더를 이용해 검증.
/// FindModelFolder와 IsModelInstalled는 실제 %AppData%에 의존하므로 테스트하지 않고,
/// 경로 주입이 가능한 internal HasRequiredFilesAt로 핵심 판정 로직만 검증한다.
/// </summary>
public class EmbeddingModelLocatorTests : IDisposable
{
    private const string OnnxFile = "model.onnx";
    private const string TokenizerFile = "sentencepiece.bpe.model";

    private readonly string _tempDir;

    public EmbeddingModelLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JangJangTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // 테스트 정리 실패는 다른 테스트 결과를 가리지 않도록 조용히 무시
        }
    }

    [Fact]
    public void GetStandardPath_ContainsExpectedSegments()
    {
        var path = EmbeddingModelLocator.GetStandardPath();

        Assert.Contains("JangJang", path);
        Assert.Contains("Models", path);
        Assert.Contains("multilingual-e5-small", path);
    }

    [Fact]
    public void HasRequiredFilesAt_NonExistentFolder_ReturnsFalse()
    {
        var fakePath = Path.Combine(_tempDir, "does-not-exist");

        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(fakePath));
    }

    [Fact]
    public void HasRequiredFilesAt_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(string.Empty));
        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(null!));
    }

    [Fact]
    public void HasRequiredFilesAt_EmptyFolder_ReturnsFalse()
    {
        // _tempDir는 생성자에서 만들어졌지만 파일이 없는 상태
        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(_tempDir));
    }

    [Fact]
    public void HasRequiredFilesAt_OnlyOnnxFile_ReturnsFalse()
    {
        // 부분 설치: model.onnx만 있고 토크나이저 없음
        File.WriteAllText(Path.Combine(_tempDir, OnnxFile), "dummy");

        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(_tempDir));
    }

    [Fact]
    public void HasRequiredFilesAt_OnlyTokenizerFile_ReturnsFalse()
    {
        // 부분 설치: 토크나이저만 있고 model.onnx 없음
        File.WriteAllText(Path.Combine(_tempDir, TokenizerFile), "dummy");

        Assert.False(EmbeddingModelLocator.HasRequiredFilesAt(_tempDir));
    }

    [Fact]
    public void HasRequiredFilesAt_BothFiles_ReturnsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, OnnxFile), "dummy");
        File.WriteAllText(Path.Combine(_tempDir, TokenizerFile), "dummy");

        Assert.True(EmbeddingModelLocator.HasRequiredFilesAt(_tempDir));
    }
}
