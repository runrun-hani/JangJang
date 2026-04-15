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
    private const int TopNCandidates = 5;

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
                return string.Empty; // 호출자가 폴백 처리

            // 4. 최종 한 줄
            return _outputProcessor.Process(candidates, fullContext);
        }
        catch
        {
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
}
