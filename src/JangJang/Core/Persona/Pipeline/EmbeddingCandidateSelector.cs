using JangJang.Core.Persona.Embedding;

namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// 임베딩 기반 후보 선정.
///
/// 씨앗 대사마다 다음 규칙으로 매칭용 텍스트를 결정:
///   - SituationDescription이 있으면 → 그 설명을 임베딩 (B 모드)
///   - 없으면 → 대사 본문 자체를 임베딩 (C 모드)
///
/// 두 모드는 한 페르소나 안에서 자동으로 혼용 가능 (대사별 개별 판단).
///
/// 임베딩은 EmbeddingCache로 영속화되어 앱 시작 시 재계산을 피한다.
/// 새로운/변경된 씨앗 대사만 새로 계산.
/// </summary>
public sealed class EmbeddingCandidateSelector : ICandidateSelector
{
    private readonly OnnxEmbeddingService _embedder;
    private readonly PersonaData _persona;
    private readonly Dictionary<long, float[]> _embeddings;
    private readonly string _cachePath;
    private bool _cacheDirty;

    public EmbeddingCandidateSelector(
        OnnxEmbeddingService embedder,
        PersonaData persona,
        string cachePath)
    {
        _embedder = embedder;
        _persona = persona;
        _cachePath = cachePath;

        // 캐시 로드 + 누락된 씨앗 대사 즉시 계산
        _embeddings = EmbeddingCache.Load(_cachePath, _embedder.Dimension);
        EnsureAllSeedEmbeddings();
        if (_cacheDirty)
        {
            EmbeddingCache.Save(_cachePath, _embedder.Dimension, _embeddings);
            _cacheDirty = false;
        }
    }

    /// <summary>
    /// 매칭용 텍스트 결정 — 설명 우선, 없으면 본문.
    /// 빈 문자열이면 매칭 불가능 → null 반환.
    /// </summary>
    private static string? GetMatchingText(SeedLine line)
    {
        if (!string.IsNullOrWhiteSpace(line.SituationDescription))
            return line.SituationDescription.Trim();
        if (!string.IsNullOrWhiteSpace(line.Text))
            return line.Text.Trim();
        return null;
    }

    /// <summary>
    /// 풀의 모든 씨앗 대사에 대해 임베딩이 캐시에 있는지 확인하고, 없으면 계산.
    /// </summary>
    private void EnsureAllSeedEmbeddings()
    {
        foreach (var line in _persona.SeedLines)
        {
            var text = GetMatchingText(line);
            if (text == null) continue;
            var hash = EmbeddingCache.ComputeTextHash(text);
            if (_embeddings.ContainsKey(hash)) continue;
            try
            {
                _embeddings[hash] = _embedder.EmbedPassage(text);
                _cacheDirty = true;
            }
            catch
            {
                // 한 대사 실패는 무시. 나머지는 계속 계산.
            }
        }
    }

    public IReadOnlyList<SeedLine> Select(string narration, DialogueContext context, int topN)
    {
        if (_persona.SeedLines.Count == 0)
            return Array.Empty<SeedLine>();
        if (string.IsNullOrWhiteSpace(narration))
            return Array.Empty<SeedLine>();

        // 새로 추가된 씨앗 대사가 있을 수 있음 (런타임 갱신 대비)
        EnsureAllSeedEmbeddings();
        if (_cacheDirty)
        {
            EmbeddingCache.Save(_cachePath, _embedder.Dimension, _embeddings);
            _cacheDirty = false;
        }

        float[] queryVec;
        try
        {
            queryVec = _embedder.EmbedQuery(narration);
        }
        catch
        {
            return Array.Empty<SeedLine>();
        }

        var scored = new List<(SeedLine Line, float Score)>(_persona.SeedLines.Count);
        foreach (var line in _persona.SeedLines)
        {
            var text = GetMatchingText(line);
            if (text == null) continue;
            var hash = EmbeddingCache.ComputeTextHash(text);
            if (!_embeddings.TryGetValue(hash, out var vec)) continue;
            var score = OnnxEmbeddingService.CosineSimilarity(queryVec, vec);
            scored.Add((line, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        var n = Math.Min(topN, scored.Count);
        var result = new List<SeedLine>(n);
        for (int i = 0; i < n; i++)
            result.Add(scored[i].Line);
        return result;
    }
}
