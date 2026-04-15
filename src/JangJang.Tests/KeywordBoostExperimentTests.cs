using System.IO;
using JangJang.Core.Persona.Embedding;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// Keyword overlap boost 실험 — 전통적 hybrid retrieval 접근.
/// Raw cosine에 쿼리-씨앗 간 어휘 중복 점수를 더해 검색 품질을 개선할 수 있는지 검증.
///
/// 관찰: e5-small의 한국어 매칭은 어휘 중복 강한 경우에만 안정적으로 동작.
/// 가설: 명시적 keyword bonus로 어휘 중복을 최대한 활용하면 top-3 개선 가능.
/// </summary>
public class KeywordBoostExperimentTests
{
    private readonly ITestOutputHelper _out;
    public KeywordBoostExperimentTests(ITestOutputHelper output) { _out = output; }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable() && File.Exists(ProbeFlagPath);

    /// <summary>
    /// 간단한 한국어 공백 기반 토큰화. 공백 단위로 잘라 정규화.
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        return text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':' },
                          StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim().ToLowerInvariant())
                   .Where(s => s.Length > 0)
                   .ToHashSet();
    }

    /// <summary>
    /// Jaccard 유사도 — 토큰 집합 교집합/합집합.
    /// </summary>
    private static float JaccardSim(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0f;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return (float)intersection / union;
    }

    [SkippableFact]
    public void Probe_KeywordBoostABComparison()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        var seeds = new[]
        {
            "힘들지? 잠깐 쉬어",
            "점심 맛있게 먹어",
            "오늘 날씨 좋다",
            "고양이는 귀엽다",
            "잘자",
            "수학 문제가 어렵다",
            "음악 듣는 거 좋아해",
            "오늘도 화이팅",
            "배가 고프다",
            "바다에 가고 싶다",
            "책을 읽어볼까",
            "커피 한잔 마시자",
            "피곤해 보이네",
            "영화 보러 가자",
            "운동 좀 해야겠다"
        };

        var testCases = new[]
        {
            ("수학 공부가 어렵네", "수학 문제가 어렵다"),
            ("배고파서 밥 먹고 싶다", "배가 고프다"),
            ("오래 앉아 있어서 피곤하다", "피곤해 보이네"),
            ("밤에 자기 전 인사", "잘자"),
            ("음악 감상", "음악 듣는 거 좋아해"),
            ("여행 가고 싶다", "바다에 가고 싶다"),
            ("커피 마시고 싶다", "커피 한잔 마시자"),
            ("책 읽기", "책을 읽어볼까"),
            ("영화 보고 싶어", "영화 보러 가자"),
            ("운동하자", "운동 좀 해야겠다"),
        };

        using var embedder = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);
        var seedVectors = seeds.Select(s => (text: s, vec: embedder.EmbedPassage(s), tokens: Tokenize(s))).ToList();

        // 여러 boost weight로 A/B/C/D
        var weights = new[] { 0.0f, 0.05f, 0.10f, 0.20f, 0.50f };

        _out.WriteLine("=== Keyword Boost Weight Sweep ===");
        _out.WriteLine("| weight | top-1 | top-3 |");
        _out.WriteLine("|---|---|---|");

        foreach (var weight in weights)
        {
            int top1 = 0, top3 = 0;
            foreach (var (query, expected) in testCases)
            {
                var queryVec = embedder.EmbedQuery(query);
                var queryTokens = Tokenize(query);

                var ranked = seedVectors
                    .Select(sv =>
                    {
                        var cosineScore = OnnxEmbeddingService.CosineSimilarity(queryVec, sv.vec);
                        var keywordScore = JaccardSim(queryTokens, sv.tokens);
                        return (sv.text, score: cosineScore + weight * keywordScore);
                    })
                    .OrderByDescending(x => x.score)
                    .ToList();

                var rank = ranked.FindIndex(x => x.text == expected);
                if (rank == 0) top1++;
                if (rank >= 0 && rank < 3) top3++;
            }
            _out.WriteLine($"| {weight:F2} | {top1}/{testCases.Length} | {top3}/{testCases.Length} |");
        }

        _out.WriteLine("");
        _out.WriteLine("해석:");
        _out.WriteLine("  weight 0.0 = 순수 cosine (기준선)");
        _out.WriteLine("  weight 상승 → keyword overlap 영향 증가");
        _out.WriteLine("  최적 weight는 top-1과 top-3를 동시에 극대화하는 값");
    }
}
