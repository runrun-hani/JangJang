using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace JangJang.Core.Persona.Embedding;

/// <summary>
/// multilingual-e5-small ONNX 모델 기반 임베딩 서비스.
///
/// 기대 모델 폴더 구조:
///   modelFolder/
///   ├── model.onnx                 (ONNX 모델 파일)
///   └── sentencepiece.bpe.model    (XLM-RoBERTa SentencePiece 토크나이저)
///
/// e5 prefix 규약:
///   - 검색 쿼리(상황 서술문)는 "query: " 접두
///   - 검색 대상(씨앗 대사)는 "passage: " 접두
///   이 규약을 지키지 않으면 매칭 품질이 크게 떨어진다.
///
/// 출력 벡터는 mean-pooled + L2-normalized 384차원(e5-small 기준).
/// </summary>
public sealed class OnnxEmbeddingService : IDisposable
{
    public const string ModelFileName = "model.onnx";
    public const string TokenizerFileName = "sentencepiece.bpe.model";
    public const int MaxSequenceLength = 512;

    private readonly InferenceSession _session;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly int _hiddenSize;
    private readonly bool _needsTokenTypeIds;
    private bool _disposed;

    /// <summary>임베딩 벡터 차원 수 (e5-small 기준 384)</summary>
    public int Dimension => _hiddenSize;

    /// <summary>
    /// 주어진 폴더에서 모델 + 토크나이저를 로드한다.
    /// 파일이 없거나 로드 실패 시 예외를 던진다 — 호출자(PersonaDialogueProvider)는
    /// 예외를 잡아 DefaultDialogueProvider로 폴백해야 한다.
    ///
    /// 생성자 안에서 _session을 먼저 할당한 뒤 토크나이저를 로드하는 구조상,
    /// 토크나이저 로드가 실패하면 _session이 네이티브 자원 누수가 될 수 있다.
    /// 본문 전체를 try로 감싸고 실패 시 수동으로 _session을 해제한다.
    /// (C#은 생성자 예외 시 IDisposable을 자동 호출하지 않는다.)
    /// </summary>
    public OnnxEmbeddingService(string modelFolderPath)
    {
        var modelPath = Path.Combine(modelFolderPath, ModelFileName);
        var tokenizerPath = Path.Combine(modelFolderPath, TokenizerFileName);

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNX 모델 파일이 없습니다: {modelPath}");
        if (!File.Exists(tokenizerPath))
            throw new FileNotFoundException($"토크나이저 파일이 없습니다: {tokenizerPath}");

        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(modelPath);

            // 토크나이저 로드. e5는 BOS=<s>, EOS=</s>를 모두 사용하는 XLM-RoBERTa 규약.
            SentencePieceTokenizer tokenizer;
            using (var tokenizerStream = File.OpenRead(tokenizerPath))
            {
                tokenizer = SentencePieceTokenizer.Create(
                    tokenizerStream,
                    addBeginningOfSentence: true,
                    addEndOfSentence: true);
            }

            // 이 시점까지 모두 성공 → 필드에 커밋
            _session = session;
            _tokenizer = tokenizer;
            _needsTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
            _hiddenSize = InferHiddenSize(_session) ?? 384;
            session = null; // 성공, finally에서 해제하지 않도록
        }
        finally
        {
            // 필드에 커밋되기 전 실패하면 session은 여전히 로컬 변수 → 여기서 해제
            session?.Dispose();
        }
    }

    private static int? InferHiddenSize(InferenceSession session)
    {
        try
        {
            var firstOutput = session.OutputMetadata.Values.FirstOrDefault();
            if (firstOutput == null) return null;
            var dims = firstOutput.Dimensions;
            if (dims == null || dims.Length == 0) return null;
            var last = dims[dims.Length - 1];
            return last > 0 ? last : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 검색 쿼리(상황 서술문) 임베딩. e5 "query: " 접두 자동 적용.
    /// </summary>
    public float[] EmbedQuery(string text) => Embed("query: " + (text ?? string.Empty));

    /// <summary>
    /// 검색 대상(씨앗 대사) 임베딩. e5 "passage: " 접두 자동 적용.
    /// </summary>
    public float[] EmbedPassage(string text) => Embed("passage: " + (text ?? string.Empty));

    private float[] Embed(string text)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OnnxEmbeddingService));

        // 1. 토큰화 → ID 리스트
        var tokenIds = _tokenizer.EncodeToIds(text);
        var seqLen = Math.Min(tokenIds.Count, MaxSequenceLength);
        if (seqLen == 0)
        {
            // 빈 입력. 영벡터 반환.
            return new float[_hiddenSize];
        }

        // 2. int64 배열 + attention mask 준비
        var inputIds = new long[seqLen];
        var attentionMask = new long[seqLen];
        for (int i = 0; i < seqLen; i++)
        {
            inputIds[i] = tokenIds[i];
            attentionMask[i] = 1;
        }

        // 3. 텐서 (batch=1)
        var dims = new[] { 1, seqLen };
        var inputIdsTensor = new DenseTensor<long>(inputIds, dims);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, dims);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        if (_needsTokenTypeIds)
        {
            var tokenTypeIds = new long[seqLen]; // 모두 0
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, dims);
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor));
        }

        // 4. 추론 실행
        using var results = _session.Run(inputs);
        var firstResult = results.First();
        var lastHidden = firstResult.AsTensor<float>();
        // shape: [1, seqLen, hiddenSize]

        // 5. Mean pooling (attention mask 가중치 적용 — 여기선 모든 토큰이 valid이므로 단순 평균)
        var pooled = new float[_hiddenSize];
        for (int t = 0; t < seqLen; t++)
        {
            for (int h = 0; h < _hiddenSize; h++)
            {
                pooled[h] += lastHidden[0, t, h];
            }
        }
        for (int h = 0; h < _hiddenSize; h++)
        {
            pooled[h] /= seqLen;
        }

        // 6. L2 정규화
        double norm = 0;
        for (int h = 0; h < _hiddenSize; h++)
        {
            norm += pooled[h] * pooled[h];
        }
        norm = Math.Sqrt(norm);
        if (norm > 0)
        {
            var inv = (float)(1.0 / norm);
            for (int h = 0; h < _hiddenSize; h++)
            {
                pooled[h] *= inv;
            }
        }

        return pooled;
    }

    /// <summary>
    /// 두 L2-정규화된 벡터의 코사인 유사도. 정규화되어 있으므로 단순 dot product와 동일.
    /// 결과 범위: [-1, 1]. 1에 가까울수록 유사.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (b == null) throw new ArgumentNullException(nameof(b));
        if (a.Length != b.Length)
            throw new ArgumentException($"벡터 길이가 다름: {a.Length} vs {b.Length}");

        float dot = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }
        return dot;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session?.Dispose();
        _disposed = true;
    }
}
