using System.IO;
using JangJang.Core.Persona.Embedding;

namespace JangJang.Tests;

/// <summary>
/// Tier 2 통합 테스트용 모델 폴더 위치 탐색.
/// 표준 위치(%AppData%/JangJang/Models/multilingual-e5-small/)에
/// 모델 파일과 토크나이저가 모두 존재해야 "사용 가능" 판정.
/// 없으면 해당 테스트는 SkippableFact로 건너뛴다.
/// </summary>
internal static class ModelFolderLocator
{
    public const string FolderName = "multilingual-e5-small";

    public static string StandardPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JangJang", "Models", FolderName);

    /// <summary>
    /// 모델 폴더가 실제로 사용 가능한지. model.onnx + sentencepiece.bpe.model 모두 존재해야 true.
    /// </summary>
    public static bool IsAvailable()
    {
        var modelPath = Path.Combine(StandardPath, OnnxEmbeddingService.ModelFileName);
        var tokenizerPath = Path.Combine(StandardPath, OnnxEmbeddingService.TokenizerFileName);
        return File.Exists(modelPath) && File.Exists(tokenizerPath);
    }

    public static string MissingReason()
    {
        if (!Directory.Exists(StandardPath))
            return $"Tier 2 통합 테스트 스킵: 모델 폴더 없음. tools/fetch-test-model.ps1로 다운로드하거나 수동으로 {StandardPath} 배치 필요";
        var modelPath = Path.Combine(StandardPath, OnnxEmbeddingService.ModelFileName);
        if (!File.Exists(modelPath))
            return $"Tier 2 통합 테스트 스킵: {OnnxEmbeddingService.ModelFileName} 없음";
        var tokenizerPath = Path.Combine(StandardPath, OnnxEmbeddingService.TokenizerFileName);
        if (!File.Exists(tokenizerPath))
            return $"Tier 2 통합 테스트 스킵: {OnnxEmbeddingService.TokenizerFileName} 없음";
        return "Tier 2 통합 테스트 스킵: 알 수 없는 이유";
    }
}
