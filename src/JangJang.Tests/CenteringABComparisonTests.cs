using System.IO;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Embedding;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// Centering ON vs OFF A/B 비교 — centering이 실제 rank 품질에 도움인지 측정.
///
/// 가설: centering은 in-corpus 전반적 분산을 늘리지만, 작은 corpus(10개)의
/// 평균을 빼면 "페르소나 톤 방향"이 제거되어 오히려 쿼리 매칭에 해로울 수 있음.
///
/// 방법: 같은 seed pool과 test queries에 대해 centered / non-centered 양쪽으로
/// 매칭한 뒤 top-1/top-3 적중률 비교. 차이가 의미 있으면 production 기본값 결정.
/// </summary>
public class CenteringABComparisonTests
{
    private readonly ITestOutputHelper _out;
    public CenteringABComparisonTests(ITestOutputHelper output) { _out = output; }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable() && File.Exists(ProbeFlagPath);

    [SkippableFact]
    public void Probe_CenteringABComparison()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        // 주제별로 명확히 다른 씨앗 풀 — 확장판 (15개)
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

        // 어휘 거의 동일 vs 의미는 같지만 어휘 다른 케이스를 섞음
        var testCases = new[]
        {
            // (query, expected, 어휘중복정도)
            ("수학 공부가 어렵네", "수학 문제가 어렵다", "높음"),
            ("배고파서 밥 먹고 싶다", "배가 고프다", "중간"),
            ("오래 앉아 있어서 피곤하다", "피곤해 보이네", "중간"),
            ("밤에 자기 전 인사", "잘자", "낮음"),
            ("음악 감상", "음악 듣는 거 좋아해", "중간"),
            ("여행 가고 싶다", "바다에 가고 싶다", "낮음"),
            ("커피 마시고 싶다", "커피 한잔 마시자", "높음"),
            ("책 읽기", "책을 읽어볼까", "높음"),
            ("영화 보고 싶어", "영화 보러 가자", "높음"),
            ("운동하자", "운동 좀 해야겠다", "높음"),
        };

        using var embedder = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        // 모든 씨앗 임베딩 + 평균 벡터 계산
        var seedVectors = seeds.Select(s => (text: s, vec: embedder.EmbedPassage(s))).ToList();

        var dim = embedder.Dimension;
        var mean = new float[dim];
        foreach (var (_, v) in seedVectors)
            for (int i = 0; i < dim; i++)
                mean[i] += v[i];
        for (int i = 0; i < dim; i++) mean[i] /= seedVectors.Count;

        // Centered 버전
        var centeredSeeds = seedVectors.Select(sv => (text: sv.text, vec: Center(sv.vec, mean))).ToList();

        _out.WriteLine("=== Centering A/B 비교 ===");
        _out.WriteLine($"씨앗 풀: {seeds.Length}개");
        _out.WriteLine($"테스트 쿼리: {testCases.Length}개");
        _out.WriteLine("");
        _out.WriteLine("| 케이스 | 어휘중복 | Raw (top1) | Raw (top3) | Centered (top1) | Centered (top3) |");
        _out.WriteLine("|---|---|---|---|---|---|");

        int rawTop1 = 0, rawTop3 = 0, cenTop1 = 0, cenTop3 = 0;

        foreach (var (query, expected, overlap) in testCases)
        {
            var queryVec = embedder.EmbedQuery(query);
            var queryCentered = Center(queryVec, mean);

            // Raw
            var rawRanked = seedVectors
                .Select(sv => (sv.text, score: OnnxEmbeddingService.CosineSimilarity(queryVec, sv.vec)))
                .OrderByDescending(x => x.score)
                .ToList();
            var rawRank = rawRanked.FindIndex(x => x.text == expected);

            // Centered
            var cenRanked = centeredSeeds
                .Select(sv => (sv.text, score: OnnxEmbeddingService.CosineSimilarity(queryCentered, sv.vec)))
                .OrderByDescending(x => x.score)
                .ToList();
            var cenRank = cenRanked.FindIndex(x => x.text == expected);

            var rT1 = rawRank == 0 ? "✓" : "·";
            var rT3 = rawRank >= 0 && rawRank < 3 ? "✓" : "·";
            var cT1 = cenRank == 0 ? "✓" : "·";
            var cT3 = cenRank >= 0 && cenRank < 3 ? "✓" : "·";

            if (rawRank == 0) rawTop1++;
            if (rawRank < 3) rawTop3++;
            if (cenRank == 0) cenTop1++;
            if (cenRank < 3) cenTop3++;

            _out.WriteLine($"| \"{query}\" | {overlap} | {rT1} #{rawRank + 1} | {rT3} | {cT1} #{cenRank + 1} | {cT3} |");
        }

        _out.WriteLine("");
        _out.WriteLine($"=== 합계 ===");
        _out.WriteLine($"Raw       : top-1 {rawTop1}/{testCases.Length}, top-3 {rawTop3}/{testCases.Length}");
        _out.WriteLine($"Centered  : top-1 {cenTop1}/{testCases.Length}, top-3 {cenTop3}/{testCases.Length}");
        _out.WriteLine("");
        if (cenTop1 > rawTop1) _out.WriteLine("→ Centering이 top-1 적중률 개선");
        else if (cenTop1 < rawTop1) _out.WriteLine("→ ⚠️ Centering이 top-1 악화");
        else _out.WriteLine("→ Centering이 top-1에 영향 없음");

        if (cenTop3 > rawTop3) _out.WriteLine("→ Centering이 top-3 적중률 개선");
        else if (cenTop3 < rawTop3) _out.WriteLine("→ ⚠️ Centering이 top-3 악화");
        else _out.WriteLine("→ Centering이 top-3에 영향 없음");
    }

    private static float[] Center(float[] v, float[] mean)
    {
        var dim = v.Length;
        var c = new float[dim];
        for (int i = 0; i < dim; i++) c[i] = v[i] - mean[i];
        double n = 0;
        foreach (var x in c) n += x * x;
        n = Math.Sqrt(n);
        if (n > 0)
        {
            var inv = (float)(1.0 / n);
            for (int i = 0; i < dim; i++) c[i] *= inv;
        }
        return c;
    }
}
