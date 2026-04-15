using System.IO;
using JangJang.Core.Persona.Embedding;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// DenseTensor&lt;T&gt; multi-dim indexer 검증 — BOS와 EOS 위치의 vector가 같아 보이는
/// 이상 현상 추적. 가설: `tensor[0, t, h]` indexer가 stride를 잘못 적용해 모든 t에
/// 같은 position을 읽고 있음.
///
/// 검증 방법: flat buffer를 ToArray()로 직접 가져와서 수동 stride 계산으로 같은 위치를
/// 읽고 비교. 두 방식의 결과가 다르면 indexer 버그 확정.
/// </summary>
public class TensorIndexingDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public TensorIndexingDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable() && File.Exists(ProbeFlagPath);

    [SkippableFact]
    public void Probe_CompareIndexerVsFlatBuffer()
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

        var ids = tokenizer.EncodeToIds("passage: 고양이는 귀엽다");
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
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[count], dims)));

        using var results = session.Run(inputs);
        var first = results.First();
        var tensor = first.AsTensor<float>();

        _out.WriteLine($"Tensor 타입: {tensor.GetType().FullName}");
        _out.WriteLine($"Dimensions: [{string.Join(",", tensor.Dimensions.ToArray())}]");
        _out.WriteLine($"Length (총 원소 수): {tensor.Length}");
        _out.WriteLine("");

        // 방법 A: Indexer
        _out.WriteLine("=== 방법 A: tensor[0, t, h] 인덱서 ===");
        for (int t = 0; t < count; t++)
        {
            var first3 = new float[] { tensor[0, t, 0], tensor[0, t, 1], tensor[0, t, 2] };
            _out.WriteLine($"  position[{t}]: [{string.Join(", ", first3.Select(f => f.ToString("F4")))}]");
        }

        // 방법 B: ToArray + manual stride
        _out.WriteLine("");
        _out.WriteLine("=== 방법 B: ToArray() + 수동 stride (batch*seq*hidden + seq*hidden 레이아웃 가정) ===");
        var flat = tensor.ToArray();
        _out.WriteLine($"Flat buffer 길이: {flat.Length} (예상: 1 * {count} * 384 = {count * 384})");
        const int hidden = 384;
        for (int t = 0; t < count; t++)
        {
            // offset = batch * (seqLen * hidden) + t * hidden + h
            var baseOffset = t * hidden;
            var first3 = new float[] { flat[baseOffset], flat[baseOffset + 1], flat[baseOffset + 2] };
            _out.WriteLine($"  position[{t}]: [{string.Join(", ", first3.Select(f => f.ToString("F4")))}]");
        }

        // 방법 C: GetEnumerator (순서 보장 확인)
        _out.WriteLine("");
        _out.WriteLine("=== 방법 C: 1D 인덱서 (tensor[i]의 i번째 float) ===");
        // tensor[linearIndex] 형태로 시도
        try
        {
            // Tensor<T>는 int flat index indexer 지원 가능
            var testOffset = 0; // position[0][0..2]
            var via1d = new[] { tensor.GetValue(0), tensor.GetValue(1), tensor.GetValue(2) };
            _out.WriteLine($"  GetValue(0,1,2): [{string.Join(", ", via1d.Select(f => f.ToString("F4")))}]");
        }
        catch (Exception ex)
        {
            _out.WriteLine($"  GetValue 실패: {ex.Message}");
        }
    }

    [SkippableFact]
    public void Probe_FlatBufferMeanPooling_AllPairsCosine()
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

        var sentences = new[]
        {
            "안녕하세요",
            "고양이는 귀엽다",
            "오늘 날씨가 좋다",
            "프로그래밍은 재미있다",
            "바다에 가고 싶어요",
            "책을 읽는 것을 좋아해요",
            "피곤해서 잠이 와",
            "수학 문제가 어려워",
            "배가 고파서 밥을 먹어야 해",
            "음악 듣는 게 취미야"
        };

        _out.WriteLine("=== Flat buffer 기반 Embed + Mean Pooling ===");
        var vectors = new List<float[]>();
        foreach (var text in sentences)
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
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[count], dims)));

            using var results = session.Run(inputs);
            var flat = results.First().AsTensor<float>().ToArray();

            const int hidden = 384;
            var pooled = new float[hidden];
            for (int t = 0; t < count; t++)
            {
                var baseOffset = t * hidden;
                for (int h = 0; h < hidden; h++)
                    pooled[h] += flat[baseOffset + h];
            }
            for (int h = 0; h < hidden; h++) pooled[h] /= count;

            double norm = 0;
            foreach (var p in pooled) norm += p * p;
            norm = Math.Sqrt(norm);
            if (norm > 0)
                for (int h = 0; h < hidden; h++) pooled[h] = (float)(pooled[h] / norm);

            vectors.Add(pooled);
        }

        var sims = new List<float>();
        for (int i = 0; i < vectors.Count; i++)
            for (int j = i + 1; j < vectors.Count; j++)
                sims.Add(OnnxEmbeddingService.CosineSimilarity(vectors[i], vectors[j]));

        var mean = sims.Average();
        var stdDev = Math.Sqrt(sims.Select(s => (s - mean) * (s - mean)).Average());
        _out.WriteLine($"평균 cosine: {mean:F4}");
        _out.WriteLine($"표준편차: {stdDev:F4}");
        _out.WriteLine($"min: {sims.Min():F4}, max: {sims.Max():F4}");
        _out.WriteLine("");
        _out.WriteLine("비교: Indexer 방식 평균 0.8867 / stddev 0.0153 였음");
        _out.WriteLine("Flat buffer 방식이 더 낮은 평균 + 더 높은 stddev면 → indexer 버그 확정");
    }
}
