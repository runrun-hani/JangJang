using System.IO;
using JangJang.Core.Persona.Embedding;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// ONNX 모델 메타데이터 + 추론 파이프라인 저수준 조사.
/// 목적: 임베딩 수렴(평균 cosine 0.88)의 원인 탐색.
///
/// 가설 후보:
///   H1. 우리가 "last_hidden_state"가 아닌 다른 출력(예: pooler_output)을 읽음
///   H2. 출력이 여러 개인데 첫 번째가 우리가 원하는 것이 아님
///   H3. BOS/EOS 임베딩이 평균을 지배해서 수렴 (짧은 문장에서 비중 ~20%)
///   H4. Mean pooling 자체는 OK인데 attention_mask 없이 전체 평균이라 뭔가 빠짐
///   H5. 실제 e5 모델이 [CLS] 토큰 풀링을 쓰는데 우리가 mean pool
/// </summary>
public class OnnxMetadataDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public OnnxMetadataDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable() && File.Exists(ProbeFlagPath);

    [SkippableFact]
    public void Probe_InspectOnnxInputOutputMetadata()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        var modelPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.ModelFileName);
        using var session = new InferenceSession(modelPath);

        _out.WriteLine("=== ONNX 모델 입력 메타데이터 ===");
        foreach (var kv in session.InputMetadata)
        {
            var md = kv.Value;
            _out.WriteLine($"[입력] {kv.Key}");
            _out.WriteLine($"  Type: {md.ElementType}");
            _out.WriteLine($"  Shape (dims): [{string.Join(", ", md.Dimensions)}]");
            _out.WriteLine($"  SymbolicDimensions: [{string.Join(", ", md.SymbolicDimensions ?? Array.Empty<string>())}]");
            _out.WriteLine($"  IsTensor: {md.IsTensor}");
        }

        _out.WriteLine("");
        _out.WriteLine("=== ONNX 모델 출력 메타데이터 ===");
        int idx = 0;
        foreach (var kv in session.OutputMetadata)
        {
            _out.WriteLine($"[출력 #{idx}] {kv.Key}");
            _out.WriteLine($"  Type: {kv.Value.ElementType}");
            _out.WriteLine($"  Shape (dims): [{string.Join(", ", kv.Value.Dimensions)}]");
            _out.WriteLine($"  SymbolicDimensions: [{string.Join(", ", kv.Value.SymbolicDimensions ?? Array.Empty<string>())}]");
            idx++;
        }

        _out.WriteLine("");
        _out.WriteLine("=== 모델 ModelMetadata ===");
        try
        {
            var meta = session.ModelMetadata;
            _out.WriteLine($"GraphName: {meta.GraphName}");
            _out.WriteLine($"ProducerName: {meta.ProducerName}");
            _out.WriteLine($"Version: {meta.Version}");
            _out.WriteLine($"Description: {meta.Description}");
            _out.WriteLine($"Domain: {meta.Domain}");
            if (meta.CustomMetadataMap != null)
            {
                foreach (var kv in meta.CustomMetadataMap)
                    _out.WriteLine($"  [custom] {kv.Key}: {kv.Value}");
            }
        }
        catch (Exception ex)
        {
            _out.WriteLine($"  메타데이터 읽기 실패: {ex.Message}");
        }
    }

    [SkippableFact]
    public void Probe_RawInferenceInspection()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        var modelPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.ModelFileName);
        using var session = new InferenceSession(modelPath);

        _out.WriteLine("=== 실제 추론 결과 raw 덤프 ===");

        // 간단한 입력: "안녕" 하나
        // 토큰 ID는 위 테스트에서 확인한 값 사용. 테스트 격리를 위해 토크나이저는 재로드.
        var tokenizerPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.TokenizerFileName);
        using var tokenStream = File.OpenRead(tokenizerPath);
        var tokenizer = Microsoft.ML.Tokenizers.SentencePieceTokenizer.Create(
            tokenStream,
            addBeginningOfSentence: true,
            addEndOfSentence: true);

        // 테스트 입력 2개 — 매우 다른 의미
        var texts = new[] { "passage: 고양이는 귀엽다", "passage: 컴퓨터 프로그래밍" };

        foreach (var text in texts)
        {
            _out.WriteLine("");
            _out.WriteLine($"--- 입력: \"{text}\" ---");
            var ids = tokenizer.EncodeToIds(text);
            _out.WriteLine($"토큰 {ids.Count}개: [{string.Join(", ", ids)}]");

            var inputIds = new long[ids.Count];
            var attentionMask = new long[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                inputIds[i] = ids[i];
                attentionMask[i] = 1;
            }

            var dims = new[] { 1, ids.Count };
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dims)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, dims))
            };
            if (session.InputMetadata.ContainsKey("token_type_ids"))
            {
                var tti = new long[ids.Count];
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tti, dims)));
            }

            using var results = session.Run(inputs);
            foreach (var r in results)
            {
                var tensor = r.AsTensor<float>();
                _out.WriteLine($"  출력 '{r.Name}' shape=[{string.Join(",", tensor.Dimensions.ToArray())}]");
                // 첫 토큰 position 의 첫 5차원 (CLS/BOS 위치)
                if (tensor.Dimensions.Length == 3 && tensor.Dimensions[0] == 1)
                {
                    var first5 = new float[5];
                    for (int d = 0; d < 5; d++) first5[d] = tensor[0, 0, d];
                    _out.WriteLine($"    position[0] (BOS) 처음 5차원: [{string.Join(", ", first5.Select(f => f.ToString("F4")))}]");

                    // 마지막 position (EOS)
                    var lastPos = (int)tensor.Dimensions[1] - 1;
                    var last5 = new float[5];
                    for (int d = 0; d < 5; d++) last5[d] = tensor[0, lastPos, d];
                    _out.WriteLine($"    position[{lastPos}] (EOS) 처음 5차원: [{string.Join(", ", last5.Select(f => f.ToString("F4")))}]");

                    // 전체 시퀀스 mean pool + L2 norm
                    var hidden = (int)tensor.Dimensions[2];
                    var pooled = new float[hidden];
                    for (int t = 0; t < tensor.Dimensions[1]; t++)
                        for (int h = 0; h < hidden; h++)
                            pooled[h] += tensor[0, t, h];
                    for (int h = 0; h < hidden; h++) pooled[h] /= tensor.Dimensions[1];
                    double norm = 0;
                    foreach (var p in pooled) norm += p * p;
                    _out.WriteLine($"    mean pool L2 norm (정규화 전): {Math.Sqrt(norm):F4}");

                    // CLS-only (position 0) L2
                    double clsNorm = 0;
                    for (int h = 0; h < hidden; h++) clsNorm += tensor[0, 0, h] * tensor[0, 0, h];
                    _out.WriteLine($"    position[0]만 L2 norm: {Math.Sqrt(clsNorm):F4}");
                }
                else if (tensor.Dimensions.Length == 2 && tensor.Dimensions[0] == 1)
                {
                    // Already pooled output
                    var size = (int)tensor.Dimensions[1];
                    var vec = new float[Math.Min(5, size)];
                    for (int i = 0; i < vec.Length; i++) vec[i] = tensor[0, i];
                    _out.WriteLine($"    shape [1,{size}] — 이미 pooled 출력일 가능성. 처음 5: [{string.Join(", ", vec.Select(f => f.ToString("F4")))}]");

                    double norm = 0;
                    for (int i = 0; i < size; i++) norm += tensor[0, i] * tensor[0, i];
                    _out.WriteLine($"    전체 L2 norm: {Math.Sqrt(norm):F4}");
                }
            }
        }
    }

    [SkippableFact]
    public void Probe_AlternativePoolingStrategies()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        var modelPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.ModelFileName);
        using var session = new InferenceSession(modelPath);

        var tokenizerPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.TokenizerFileName);
        using var tokenStream = File.OpenRead(tokenizerPath);
        var tokenizer = Microsoft.ML.Tokenizers.SentencePieceTokenizer.Create(
            tokenStream,
            addBeginningOfSentence: true,
            addEndOfSentence: true);

        // 다양한 문장 5개 — 같은 pooling 전략으로 임베딩한 뒤 all-pairs cosine 비교
        var texts = new[]
        {
            "passage: 고양이는 귀엽다",
            "passage: 바다에 가고 싶다",
            "passage: 수학 문제가 어렵다",
            "passage: 오늘 날씨 좋다",
            "passage: 배가 고프다",
        };

        _out.WriteLine("=== 대안 Pooling 전략 비교 ===");
        _out.WriteLine("전략 A: 전체 mean pooling (현재 구현)");
        _out.WriteLine("전략 B: BOS/EOS 제외한 content 토큰만 mean pooling");
        _out.WriteLine("전략 C: CLS (position 0, = BOS) 토큰만");
        _out.WriteLine("전략 D: Masked mean (attention_mask 가중) — 여기선 A와 동일해야 함");
        _out.WriteLine("");

        var embA = new List<float[]>();
        var embB = new List<float[]>();
        var embC = new List<float[]>();

        foreach (var text in texts)
        {
            var ids = tokenizer.EncodeToIds(text);
            var count = ids.Count;
            var inputIds = new long[count];
            var mask = new long[count];
            for (int i = 0; i < count; i++) { inputIds[i] = ids[i]; mask[i] = 1; }

            var dims = new[] { 1, count };
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dims)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, dims))
            };
            if (session.InputMetadata.ContainsKey("token_type_ids"))
            {
                var tti = new long[count];
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tti, dims)));
            }

            using var results = session.Run(inputs);
            var tensor = results.First().AsTensor<float>();
            var hidden = (int)tensor.Dimensions[2];

            // A: 전체 평균
            var vecA = new float[hidden];
            for (int t = 0; t < count; t++)
                for (int h = 0; h < hidden; h++)
                    vecA[h] += tensor[0, t, h];
            for (int h = 0; h < hidden; h++) vecA[h] /= count;
            Normalize(vecA);
            embA.Add(vecA);

            // B: BOS/EOS 제외 (position 1 .. count-2)
            var vecB = new float[hidden];
            var contentCount = Math.Max(1, count - 2);
            for (int t = 1; t < count - 1; t++)
                for (int h = 0; h < hidden; h++)
                    vecB[h] += tensor[0, t, h];
            for (int h = 0; h < hidden; h++) vecB[h] /= contentCount;
            Normalize(vecB);
            embB.Add(vecB);

            // C: BOS only
            var vecC = new float[hidden];
            for (int h = 0; h < hidden; h++) vecC[h] = tensor[0, 0, h];
            Normalize(vecC);
            embC.Add(vecC);
        }

        PrintAllPairs("A (전체 mean)", embA);
        PrintAllPairs("B (content만 mean, BOS/EOS 제외)", embB);
        PrintAllPairs("C (BOS만)", embC);
    }

    private void PrintAllPairs(string strategyName, List<float[]> embs)
    {
        var sims = new List<float>();
        for (int i = 0; i < embs.Count; i++)
            for (int j = i + 1; j < embs.Count; j++)
                sims.Add(OnnxEmbeddingService.CosineSimilarity(embs[i], embs[j]));
        var mean = sims.Average();
        var min = sims.Min();
        var max = sims.Max();
        var stdDev = Math.Sqrt(sims.Select(s => (s - mean) * (s - mean)).Average());
        _out.WriteLine($"[{strategyName}] 평균={mean:F4}, stddev={stdDev:F4}, min={min:F4}, max={max:F4}, range={max - min:F4}");
    }

    private static void Normalize(float[] v)
    {
        double norm = 0;
        foreach (var x in v) norm += x * x;
        norm = Math.Sqrt(norm);
        if (norm > 0)
        {
            var inv = (float)(1.0 / norm);
            for (int i = 0; i < v.Length; i++) v[i] *= inv;
        }
    }
}
