using System.IO;
using System.Text.Json;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Embedding;
using JangJang.Core.Persona.Pipeline;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;
using Xunit.Abstractions;

namespace JangJang.Tests;

/// <summary>
/// 실제 사용자 페르소나 데이터를 로드해 매칭 rank 품질을 측정.
/// "수렴" 자체보다 상위 순위가 얼마나 의미 있는지가 실용 매칭의 핵심.
///
/// 또한 여러 pooling/post-processing 전략을 병렬 비교:
///   S1. 현재 (전체 mean pool + L2)
///   S2. Content only (BOS/EOS 제외 mean + L2)
///   S3. Mean centered (S1 + 데이터셋 평균 빼기)
///   S4. S2 + centering
/// </summary>
public class MatchQualityDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public MatchQualityDiagnosticTests(ITestOutputHelper output) { _out = output; }

    private static string ProbeFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug-probe.flag");

    private static string PersonaJsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "Personas", "current", "persona.json");

    private static bool ProbeEnabled => ModelFolderLocator.IsAvailable()
        && File.Exists(ProbeFlagPath)
        && File.Exists(PersonaJsonPath);

    [SkippableFact]
    public void Probe_RealPersonaRankQuality()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성 or persona.json 없음");

        var personaJson = File.ReadAllText(PersonaJsonPath);
        var persona = JsonSerializer.Deserialize<PersonaData>(personaJson)!;

        _out.WriteLine($"=== 실제 페르소나 로드 ===");
        _out.WriteLine($"Name: {persona.Name}");
        _out.WriteLine($"SeedLines 개수: {persona.SeedLines.Count}");
        _out.WriteLine("");

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

        // 씨앗 대사 임베딩 (SituationDescription 없으면 본문 사용 — C 모드)
        var seedVectors = new List<(SeedLine line, float[] vec)>();
        foreach (var s in persona.SeedLines)
        {
            var text = string.IsNullOrWhiteSpace(s.SituationDescription) ? s.Text : s.SituationDescription;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var v = service.EmbedPassage(text);
            seedVectors.Add((s, v));
        }

        // 여러 상황 시나리오 — ContextNarrator의 실제 출력 포맷 사용
        var narrator = new ContextNarrator();
        var scenarios = new[]
        {
            ("집중 시작 직후 오전", new DialogueContext {
                State = PetState.Happy,
                SessionSeconds = 60,
                Timestamp = new DateTime(2026, 4, 16, 10, 0, 0)
            }),
            ("3시간 집중 후 오후", new DialogueContext {
                State = PetState.Happy,
                SessionSeconds = 3 * 3600,
                Timestamp = new DateTime(2026, 4, 16, 15, 0, 0)
            }),
            ("경계 (잠깐 딴짓) 점심", new DialogueContext {
                State = PetState.Alert,
                SessionSeconds = 90 * 60,
                Timestamp = new DateTime(2026, 4, 16, 12, 30, 0)
            }),
            ("가벼운 짜증 저녁", new DialogueContext {
                State = PetState.Annoyed,
                Annoyance = 0.3,
                SessionSeconds = 120 * 60,
                Timestamp = new DateTime(2026, 4, 16, 19, 0, 0)
            }),
            ("강한 짜증 밤", new DialogueContext {
                State = PetState.Annoyed,
                Annoyance = 0.85,
                SessionSeconds = 240 * 60,
                Timestamp = new DateTime(2026, 4, 16, 23, 0, 0)
            }),
            ("수면 이른 아침", new DialogueContext {
                State = PetState.Sleeping,
                Timestamp = new DateTime(2026, 4, 16, 7, 0, 0)
            }),
            ("깨어남 오전", new DialogueContext {
                State = PetState.WakeUp,
                Timestamp = new DateTime(2026, 4, 16, 9, 0, 0)
            }),
        };

        _out.WriteLine("=== 시나리오별 상위 3개 씨앗 랭킹 ===");
        _out.WriteLine("");
        foreach (var (label, ctx) in scenarios)
        {
            var narration = narrator.Narrate(ctx);
            _out.WriteLine($"── {label} ──");
            _out.WriteLine($"Narration: {narration}");
            var queryVec = service.EmbedQuery(narration);

            var scored = seedVectors
                .Select(sv => new
                {
                    Line = sv.line,
                    Score = OnnxEmbeddingService.CosineSimilarity(queryVec, sv.vec)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            for (int i = 0; i < Math.Min(3, scored.Count); i++)
                _out.WriteLine($"  #{i + 1} [{scored[i].Score:F4}] \"{scored[i].Line.Text}\"");

            var bottom = scored[^1];
            _out.WriteLine($"  ... (꼴찌: [{bottom.Score:F4}] \"{bottom.Line.Text}\")");
            _out.WriteLine($"  마진 (1등-꼴찌): {scored[0].Score - bottom.Score:F4}");
            _out.WriteLine("");
        }

        // Rank quality 종합 지표
        _out.WriteLine("=== Rank Quality 종합 지표 ===");
        var allMargins = new List<float>();
        foreach (var (_, ctx) in scenarios)
        {
            var queryVec = service.EmbedQuery(narrator.Narrate(ctx));
            var scores = seedVectors
                .Select(sv => OnnxEmbeddingService.CosineSimilarity(queryVec, sv.vec))
                .OrderByDescending(s => s)
                .ToList();
            allMargins.Add(scores[0] - scores[^1]);
        }
        _out.WriteLine($"시나리오별 평균 마진 (1등-꼴찌): {allMargins.Average():F4}");
        _out.WriteLine($"마진 범위: {allMargins.Min():F4} ~ {allMargins.Max():F4}");
        _out.WriteLine("");
        _out.WriteLine("해석:");
        _out.WriteLine("  마진 > 0.05 → 순위가 의미 있음 (rank-based matching 작동)");
        _out.WriteLine("  마진 < 0.02 → 순위가 거의 noise (매칭 무의미)");
        _out.WriteLine("  마진 0.02-0.05 → 경계. 상위 N개 랜덤이 필수");
    }

    [SkippableFact]
    public void Probe_MeanCenteringImprovement()
    {
        Skip.IfNot(ProbeEnabled, "Probe 비활성");

        using var service = new OnnxEmbeddingService(ModelFolderLocator.StandardPath);

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

        _out.WriteLine("=== Mean Centering 개선 실험 ===");
        _out.WriteLine("가설: 모든 e5 벡터가 비슷한 방향으로 편향 → 데이터셋 평균을 빼면 분산 증가");
        _out.WriteLine("");

        var vecs = sentences.Select(s => service.EmbedPassage(s)).ToList();

        // 원본 all-pairs
        PrintCosineStats("S1 (원본)", vecs);

        // 평균 벡터 계산 + 빼기 + 재정규화
        var dim = vecs[0].Length;
        var mean = new float[dim];
        foreach (var v in vecs)
            for (int i = 0; i < dim; i++)
                mean[i] += v[i];
        for (int i = 0; i < dim; i++) mean[i] /= vecs.Count;

        var centered = vecs.Select(v =>
        {
            var c = new float[dim];
            for (int i = 0; i < dim; i++) c[i] = v[i] - mean[i];
            // Re-normalize
            double n = 0;
            foreach (var x in c) n += x * x;
            n = Math.Sqrt(n);
            if (n > 0)
                for (int i = 0; i < dim; i++) c[i] = (float)(c[i] / n);
            return c;
        }).ToList();

        PrintCosineStats("S3 (centered + renormalized)", centered);

        _out.WriteLine("");
        _out.WriteLine("비교 해석: centering이 평균을 낮추고 stddev를 높이면 구분력 개선");
    }

    private void PrintCosineStats(string label, List<float[]> vecs)
    {
        var sims = new List<float>();
        for (int i = 0; i < vecs.Count; i++)
            for (int j = i + 1; j < vecs.Count; j++)
                sims.Add(OnnxEmbeddingService.CosineSimilarity(vecs[i], vecs[j]));
        var mean = sims.Average();
        var std = Math.Sqrt(sims.Select(s => (s - mean) * (s - mean)).Average());
        _out.WriteLine($"[{label}] 평균={mean:F4} stddev={std:F4} min={sims.Min():F4} max={sims.Max():F4}");
    }
}
