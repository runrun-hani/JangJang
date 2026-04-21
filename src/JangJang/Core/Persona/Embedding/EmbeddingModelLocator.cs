using System.IO;

namespace JangJang.Core.Persona.Embedding;

/// <summary>
/// 임베딩 모델 폴더 탐색 및 설치 상태 판정 헬퍼.
/// App 스타트업과 설정창이 동일한 판정 로직을 공유하기 위해 분리.
///
/// 필수 파일 2개:
///   - model.onnx            — ONNX 모델 파일 (~120MB)
///   - sentencepiece.bpe.model — XLM-RoBERTa SentencePiece 토크나이저 (~5MB)
///
/// 둘 중 하나라도 없으면 "미설치"로 취급 (부분 설치 허용 안 함).
/// </summary>
public static class EmbeddingModelLocator
{
    private const string ModelFolderName = "multilingual-e5-small";
    private const string OnnxFile = "model.onnx";
    private const string TokenizerFile = "sentencepiece.bpe.model";

    /// <summary>표준 배치 경로. 존재 여부와 무관하게 반환한다 (UI 표시·폴더 생성에 사용).</summary>
    public static string GetStandardPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JangJang", "Models", ModelFolderName);

    /// <summary>
    /// 모델 폴더 탐색. 표준 위치(%AppData%) → 포터블 위치(실행 파일 옆) 순서.
    /// 둘 다 없으면 null. 파일 유무는 체크하지 않으므로 존재 확인은 IsModelInstalled 사용.
    /// </summary>
    public static string? FindModelFolder()
    {
        var appDataPath = GetStandardPath();
        if (Directory.Exists(appDataPath)) return appDataPath;

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var localPath = Path.Combine(exeDir, ModelFolderName);
                if (Directory.Exists(localPath)) return localPath;
            }
        }

        return null;
    }

    /// <summary>폴더가 존재하고 필수 파일 2개(model.onnx, sentencepiece.bpe.model)가 모두 있어야 true.</summary>
    public static bool IsModelInstalled()
    {
        var folder = FindModelFolder();
        return folder != null && HasRequiredFilesAt(folder);
    }

    /// <summary>
    /// 지정된 폴더에 필수 파일 2개가 모두 있는지 확인.
    /// 경로 주입이 가능해 단위 테스트에서 임시 폴더로 검증 가능.
    /// 폴더가 없거나 파일 일부만 있으면 false (부분 설치 허용 안 함).
    /// </summary>
    internal static bool HasRequiredFilesAt(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        if (!Directory.Exists(folder)) return false;
        return File.Exists(Path.Combine(folder, OnnxFile))
            && File.Exists(Path.Combine(folder, TokenizerFile));
    }
}
