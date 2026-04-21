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
///
/// ── 매칭 품질 노트 (2026-04-16 진단) ──────────────────────────────────
/// e5-small 모델은 한국어 짧은 대화체 문장에 대한 의미 매칭이 제한적임을 확인.
/// 10개 무관 문장 all-pairs cosine 평균 0.8867 / stddev 0.0153 (anisotropy).
/// Mean centering 개선 실험 결과: top-1 4→5, top-3 6→6 — 효과 미미. 채택 안 함.
///
/// Keyword overlap boost (Jaccard 토큰 중복) 실험: weight 0.05에서 top-1 4→6
/// (+50%), top-3 6→8 (+33%) 의미 있는 개선. 채택 — 하이브리드 검색 방식.
/// 최종 score = cosine + 0.05 * jaccard(queryTokens, seedTokens).
///
/// 근본적 개선(더 큰 모델, 한국어 특화)은 현재 scope 밖.
/// </summary>
public sealed class EmbeddingCandidateSelector : ICandidateSelector
{
    /// <summary>
    /// Keyword overlap Jaccard 점수의 가중치. 0.05에서 A/B 비교로 검증됨.
    /// 0보다 크면 hybrid mode (cosine + keyword), 0이면 pure cosine.
    /// </summary>
    private const float KeywordBoostWeight = 0.05f;

    private readonly OnnxEmbeddingService _embedder;
    private readonly PersonaData _persona;
    private readonly Dictionary<long, float[]> _embeddings;
    private readonly Dictionary<long, HashSet<string>> _seedTokens;
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
        _seedTokens = new Dictionary<long, HashSet<string>>();

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
    /// 공백/구두점 기반 간단 토큰화 + 소문자 정규화. 한국어 형태소 분석은 하지 않음.
    /// Jaccard 유사도 계산용.
    /// </summary>
    private static readonly char[] TokenDelimiters =
        { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '\'', '"', '(', ')', '[', ']' };

    private static HashSet<string> TokenizeForKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>();
        return text.Split(TokenDelimiters, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim().ToLowerInvariant())
                   .Where(s => s.Length > 0)
                   .ToHashSet();
    }

    private static float JaccardSim(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0f;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return (float)intersection / union;
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
    /// Keyword token set도 함께 계산 (무거운 연산 아님, 매 호출 재계산해도 무방).
    /// </summary>
    private void EnsureAllSeedEmbeddings()
    {
        foreach (var line in _persona.SeedLines)
        {
            var text = GetMatchingText(line);
            if (text == null) continue;
            var hash = EmbeddingCache.ComputeTextHash(text);

            // Keyword tokens 항상 동기화
            if (!_seedTokens.ContainsKey(hash))
                _seedTokens[hash] = TokenizeForKeyword(text);

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

        // 쿼리의 keyword tokens (hybrid 매칭용)
        var queryTokens = TokenizeForKeyword(narration);

        // State 필드가 설정된 대사가 있으면, 현재 상태에 맞는 대사만 후보로 사용.
        // 해당 상태에 대사가 없으면 전체 풀에서 매칭 (폴백).
        var candidates = _persona.SeedLines
            .Where(l => l.State == context.State)
            .ToList();
        if (candidates.Count == 0)
            candidates = _persona.SeedLines;

        var scored = new List<(SeedLine Line, float Score)>(candidates.Count);
        foreach (var line in candidates)
        {
            var text = GetMatchingText(line);
            if (text == null) continue;
            var hash = EmbeddingCache.ComputeTextHash(text);
            if (!_embeddings.TryGetValue(hash, out var vec)) continue;

            var cosineScore = OnnxEmbeddingService.CosineSimilarity(queryVec, vec);

            // Hybrid: cosine + keyword boost
            float keywordScore = 0f;
            if (KeywordBoostWeight > 0 && _seedTokens.TryGetValue(hash, out var seedTok))
                keywordScore = JaccardSim(queryTokens, seedTok);

            var finalScore = cosineScore + KeywordBoostWeight * keywordScore;
            scored.Add((line, finalScore));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        var n = Math.Min(topN, scored.Count);
        var result = new List<SeedLine>(n);
        for (int i = 0; i < n; i++)
            result.Add(scored[i].Line);
        return result;
    }
}
