using System.IO;
using System.Text;
using JangJang.Core.Persona.Embedding;
using JangJang.Core.Persona.Pipeline;

namespace JangJang.Core.Persona;

/// <summary>
/// 페르소나 기반 대사 Provider. 4단계 파이프라인을 오케스트레이션한다:
///   1. ContextCollector  — 풍부한 상황 수집
///   2. ContextNarrator   — 상황 → 한국어 자연어 문장
///   3. CandidateSelector — 임베딩 매칭으로 상위 N개 후보 선정
///   4. OutputProcessor   — 최종 한 줄 선택 (가 MVP는 가중치 랜덤, 나는 LLM 변주)
///
/// 어느 단계든 실패하거나 후보가 비면 빈 문자열 반환 → 호출 측(Dialogue.cs)이
/// 폴백 처리. PersonaDialogueProvider 자체 생성이 실패하면 호출자가 잡아서
/// DefaultDialogueProvider로 폴백해야 한다.
/// </summary>
public sealed class PersonaDialogueProvider : IDialogueProvider, IDisposable
{
    /// <summary>
    /// 후보 수 제한 없음 — 해당 상태의 모든 대사를 스코어링하여 OutputProcessor에 전달.
    /// OutputProcessor의 가중치 랜덤 + 반복 방지가 다양성을 보장한다.
    /// </summary>
    private const int TopNCandidates = int.MaxValue;

    // ─── 진단 로깅 (임시 — 매칭 품질 디버그용) ───────────────────────────────
    // 플래그 파일이 존재할 때만 기록:
    //   %AppData%/JangJang/persona-debug.flag
    // 로그 파일:
    //   %AppData%/JangJang/persona-debug.log
    // 진단 끝나면 플래그 파일 삭제하면 로그 쓰기 중단. 이 Provider 코드에서
    // 진단 블록을 제거하려면 "DIAG-BLOCK" 주석으로 묶인 영역을 지우면 됨.
    private static readonly string DiagFlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug.flag");
    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JangJang", "persona-debug.log");
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>디버그 모드 활성 시 파이프라인 결과를 UI로 전달하는 이벤트.</summary>
    public static event Action<Pipeline.DebugEntry>? OnDebugEntry;

    private readonly OnnxEmbeddingService _embedder;
    private readonly IContextCollector _contextCollector;
    private readonly ContextNarrator _narrator;
    private readonly ICandidateSelector _candidateSelector;
    private readonly IOutputProcessor _outputProcessor;
    private bool _disposed;

    /// <summary>
    /// 통상적 생성 — 의존성을 외부에서 주입.
    /// embedder의 소유권은 이 Provider가 가짐 (Dispose 시 함께 해제).
    /// </summary>
    public PersonaDialogueProvider(
        OnnxEmbeddingService embedder,
        IContextCollector contextCollector,
        ICandidateSelector candidateSelector,
        IOutputProcessor outputProcessor)
    {
        _embedder = embedder;
        _contextCollector = contextCollector;
        _candidateSelector = candidateSelector;
        _outputProcessor = outputProcessor;
        _narrator = new ContextNarrator();
    }

    /// <summary>
    /// 표준 조립 헬퍼. 모델 폴더 + 페르소나 + ActivityMonitor만 주면 완성된 Provider 반환.
    /// 캐시 경로는 PersonaStore.EmbeddingsCachePath를 사용.
    /// 어느 단계든 실패하면 예외 전파 → 호출자가 폴백.
    /// </summary>
    public static PersonaDialogueProvider Create(
        string modelFolderPath,
        PersonaData persona,
        ActivityMonitor monitor)
    {
        var embedder = new OnnxEmbeddingService(modelFolderPath);
        try
        {
            var collector = new DefaultContextCollector(monitor);
            var selector = new EmbeddingCandidateSelector(embedder, persona, PersonaStore.EmbeddingsCachePath);
            var processor = new PassthroughOutputProcessor();
            return new PersonaDialogueProvider(embedder, collector, selector, processor);
        }
        catch
        {
            embedder.Dispose();
            throw;
        }
    }

    public string GetLine(DialogueContext basicContext)
    {
        if (_disposed)
            return string.Empty;

        try
        {
            // 1. 상황 보강
            var fullContext = _contextCollector.Collect(basicContext);

            // 2. 상황 → 한국어 서술
            var narration = _narrator.Narrate(fullContext);

            // 3. 후보 선정
            var candidates = _candidateSelector.Select(narration, fullContext, TopNCandidates);
            if (candidates.Count == 0)
            {
                WriteDiagnostic(fullContext, narration, candidates, finalLine: null); // DIAG-BLOCK
                OnDebugEntry?.Invoke(new Pipeline.DebugEntry(DateTime.Now, fullContext, narration, candidates, null));
                return string.Empty; // 호출자가 폴백 처리
            }

            // 4. 최종 한 줄
            var result = _outputProcessor.Process(candidates, fullContext);
            WriteDiagnostic(fullContext, narration, candidates, result); // DIAG-BLOCK
            OnDebugEntry?.Invoke(new Pipeline.DebugEntry(DateTime.Now, fullContext, narration, candidates, result));
            return result;
        }
        catch (Exception ex)
        {
            WriteDiagnosticException(ex); // DIAG-BLOCK
            // 파이프라인 어느 지점이든 예외 발생 시 빈 문자열 반환 → 호출자가 폴백
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _embedder?.Dispose();
        _disposed = true;
    }

    // ─── DIAG-BLOCK START ─────────────────────────────────────────────────
    // 진단 로그. persona-debug.flag 파일이 있을 때만 쓰기. 진단 완료 시
    // 이 블록(헬퍼 2개 + GetLine의 WriteDiagnostic/WriteDiagnosticException 호출 3곳)을
    // 삭제하거나 플래그 파일만 지워도 무해하게 비활성화됨.
    private static void WriteDiagnostic(
        DialogueContext ctx,
        string narration,
        IReadOnlyList<SeedLine> candidates,
        string? finalLine)
    {
        if (!File.Exists(DiagFlagPath)) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
            sb.Append($"State={ctx.State} Annoyance={ctx.Annoyance:F2} ");
            sb.Append($"Session={FormatSeconds(ctx.SessionSeconds)} ");
            sb.Append($"Idle={FormatSeconds(ctx.IdleSessionSeconds)} ");
            sb.Append($"Today={FormatSeconds(ctx.TodaySeconds)}");
            sb.AppendLine();
            sb.Append("  Narration: ").AppendLine(narration);
            sb.Append("  Candidates (top ").Append(candidates.Count).AppendLine("):");
            for (int i = 0; i < candidates.Count; i++)
            {
                sb.Append("    ").Append(i + 1).Append(". ").AppendLine(candidates[i].Text);
            }
            sb.Append("  Final: ").AppendLine(finalLine ?? "(empty — fallback)");
            sb.AppendLine();
            File.AppendAllText(DiagLogPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 로그 기록 실패는 무시 (진단이 본 동작을 깨면 안 됨)
        }
    }

    private static void WriteDiagnosticException(Exception ex)
    {
        if (!File.Exists(DiagFlagPath)) return;
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EXCEPTION: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(DiagLogPath, line, Encoding.UTF8);
        }
        catch { }
    }

    private static string FormatSeconds(int s)
    {
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m{s % 60}s";
        return $"{s / 3600}h{(s % 3600) / 60}m";
    }
    // ─── DIAG-BLOCK END ───────────────────────────────────────────────────
}
