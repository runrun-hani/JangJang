namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// 파이프라인 1회 실행의 디버그 스냅샷.
/// PersonaDialogueProvider.OnDebugEntry 이벤트를 통해 DebugWindow에 전달된다.
/// </summary>
public sealed record DebugEntry(
    DateTime Timestamp,
    DialogueContext Context,
    string Narration,
    IReadOnlyList<SeedLine> Candidates,
    string? FinalLine);
