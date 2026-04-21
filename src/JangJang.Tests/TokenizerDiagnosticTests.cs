using System.IO;
using System.Text;
using JangJang.Core.Persona.Embedding;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// 토크나이저/임베딩 품질 진단 테스트 — 2026-04-16 매칭 품질 문제 조사용.
///
/// 실행 조건: 표준 위치에 모델 + 토크나이저 파일이 있고 플래그 파일이 있을 때만.
///   플래그: %AppData%/JangJang/persona-debug-probe.flag
///
/// 이 테스트는 pass/fail을 단정하지 않고 **정보 수집**이 목적이라
/// xUnit ITestOutputHelper로 결과를 출력. `dotnet test --logger "console;verbosity=detailed"` 로 확인.
/// </summary>
public class TokenizerDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public TokenizerDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable() && File.Exists(ProbeFlagPath);

    [SkippableFact]
    public void Probe_TokenizerProducesDifferentTokensForDifferentKoreanInputs()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성 (persona-debug-probe.flag 없음 또는 모델 없음)");

        var tokenizerPath = Path.Combine(ModelFolderLocator.StandardPath, OnnxEmbeddingService.TokenizerFileName);
        using var stream = File.OpenRead(tokenizerPath);
        var tokenizer = SentencePieceTokenizer.Create(
            stream,
            addBeginningOfSentence: true,
            addEndOfSentence: true);

        _out.WriteLine("=== 토크나이저 토큰 ID 덤프 ===");
        _out.WriteLine($"Vocabulary 크기: {tokenizer.Vocabulary.Count}");
        _out.WriteLine($"BOS 토큰 ID: {tokenizer.BeginningOfSentenceId}, 토큰: {tokenizer.BeginningOfSentenceToken}");
        _out.WriteLine($"EOS 토큰 ID: {tokenizer.EndOfSentenceId}, 토큰: {tokenizer.EndOfSentenceToken}");
        _out.WriteLine($"UNK 토큰 ID: {tokenizer.UnknownId}, 토큰: {tokenizer.UnknownToken}");
        _out.WriteLine("");

        var samples = new[]
        {
            "안녕하세요",
            "고양이는 귀엽다",
            "오늘 날씨가 좋다",
            "집중해서 일하자",
            "피곤해 보이네요",
            "query: 오래 앉아서 지쳐 보인다",
            "passage: 힘들지? 잠깐 쉬어",
            "passage: 고양이는 귀엽다",
            "the quick brown fox",
            ""
        };

        var results = new List<(string text, IReadOnlyList<int> ids)>();
        foreach (var s in samples)
        {
            var ids = tokenizer.EncodeToIds(s);
            results.Add((s, ids));
            _out.WriteLine($"[{ids.Count:D3} 토큰] \"{s}\"");
            _out.WriteLine($"         IDs: [{string.Join(", ", ids.Take(20))}{(ids.Count > 20 ? ", ..." : "")}]");
        }

        // 핵심 검증: 서로 다른 입력이 같은 토큰 시퀀스를 만드는가?
        _out.WriteLine("");
        _out.WriteLine("=== 토큰 시퀀스 중복 검사 ===");
        int dupFound = 0;
        for (int i = 0; i < results.Count; i++)
        {
            for (int j = i + 1; j < results.Count; j++)
            {
                var a = results[i].ids;
                var b = results[j].ids;
                if (a.Count == b.Count && a.SequenceEqual(b))
                {
                    _out.WriteLine($"⚠️ 중복! [{i}] \"{results[i].text}\" == [{j}] \"{results[j].text}\"");
                    dupFound++;
                }
            }
        }
        if (dupFound == 0)
            _out.WriteLine("✓ 모든 입력이 서로 다른 토큰 시퀀스를 생성 (토크나이저 정상)");

        // UNK 비율 검사
        _out.WriteLine("");
        _out.WriteLine("=== UNK 토큰 비율 ===");
        foreach (var (text, ids) in results)
        {
            if (ids.Count == 0) continue;
            int unkCount = ids.Count(id => id == tokenizer.UnknownId);
            double unkRatio = (double)unkCount / ids.Count;
            var marker = unkRatio > 0.5 ? "⚠️" : (unkRatio > 0 ? "·" : "✓");
            _out.WriteLine($"{marker} \"{text}\": {unkCount}/{ids.Count} = {unkRatio:P0}");
        }
    }

    [SkippableFact]
    public void Probe_EmbeddingCollapseDetection()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        _out.WriteLine("=== 임베딩 벡터 수렴 조사 ===");
        _out.WriteLine($"모델 차원: {service.Dimension}");
        _out.WriteLine("");

        // 의미상 완전히 무관한 한국어 문장 10개
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

        var vectors = new List<float[]>();
        foreach (var s in sentences)
        {
            var vec = service.EmbedPassage(s);
            vectors.Add(vec);
            double norm = 0;
            foreach (var v in vec) norm += v * v;
            _out.WriteLine($"  \"{s}\" → L2_norm={Math.Sqrt(norm):F4}, 처음 5차원=[{string.Join(", ", vec.Take(5).Select(f => f.ToString("F4")))}]");
        }

        // all-pairs cosine
        _out.WriteLine("");
        _out.WriteLine("=== All-pairs Cosine 유사도 ===");
        var sims = new List<float>();
        for (int i = 0; i < vectors.Count; i++)
        {
            for (int j = i + 1; j < vectors.Count; j++)
            {
                var sim = OnnxEmbeddingService.CosineSimilarity(vectors[i], vectors[j]);
                sims.Add(sim);
            }
        }

        var mean = sims.Average();
        var min = sims.Min();
        var max = sims.Max();
        var stdDev = Math.Sqrt(sims.Select(s => (s - mean) * (s - mean)).Average());

        _out.WriteLine($"평균 cosine: {mean:F4}");
        _out.WriteLine($"최소: {min:F4}");
        _out.WriteLine($"최대: {max:F4}");
        _out.WriteLine($"표준편차: {stdDev:F4}");
        _out.WriteLine($"Range (max-min): {max - min:F4}");
        _out.WriteLine("");

        // 해석
        _out.WriteLine("=== 해석 ===");
        if (mean > 0.95f && stdDev < 0.02f)
            _out.WriteLine("🔴 심각한 수렴: 모든 벡터가 거의 동일. 토크나이저/임베딩 레이어 심각 문제");
        else if (mean > 0.85f && stdDev < 0.05f)
            _out.WriteLine("🟡 중간 수렴: 내용 구분이 약함. 기대 평균 0.3-0.6이지만 훨씬 높음");
        else if (mean > 0.5f)
            _out.WriteLine("⚠️ 경미한 수렴: 의미 구분이 있지만 모든 벡터가 한쪽으로 치우침");
        else
            _out.WriteLine("✓ 정상: 다양한 의미의 문장들이 서로 다른 임베딩 영역에 분포");

        _out.WriteLine("");
        _out.WriteLine("참고 — 건강한 임베딩 모델의 기대 분포:");
        _out.WriteLine("  평균: 0.3-0.5 (완전 무관 문장들 사이)");
        _out.WriteLine("  stddev: 0.1-0.2 (충분한 변별력)");
    }

    [SkippableFact]
    public void Probe_QueryVsPassagePrefixEffect()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        _out.WriteLine("=== e5 prefix 효과 조사 ===");

        // 같은 문장을 query/passage로 임베딩 후 코사인
        var text = "고양이는 귀엽다";
        var q = service.EmbedQuery(text);
        var p = service.EmbedPassage(text);
        var simSame = OnnxEmbeddingService.CosineSimilarity(q, p);
        _out.WriteLine($"같은 문장 query vs passage 코사인: {simSame:F4}");
        _out.WriteLine("  (1에 가까워야 이상적, 0.95 이하면 prefix 과민)");

        // 무관 문장 2개를 각각 passage로
        var p1 = service.EmbedPassage("고양이는 귀엽다");
        var p2 = service.EmbedPassage("오늘 저녁에 회의가 있다");
        var simPP = OnnxEmbeddingService.CosineSimilarity(p1, p2);
        _out.WriteLine($"무관 passage-passage 코사인: {simPP:F4}");

        // 관련 쿼리-패시지
        var qRel = service.EmbedQuery("귀여운 동물");
        var pRel = service.EmbedPassage("고양이는 귀엽다");
        var simRel = OnnxEmbeddingService.CosineSimilarity(qRel, pRel);
        _out.WriteLine($"관련 query-passage 코사인: {simRel:F4}");

        _out.WriteLine("");
        _out.WriteLine("해석: 관련 쿼리-패시지 > 무관 passage-passage 이면 prefix는 정상 동작");
    }
}
