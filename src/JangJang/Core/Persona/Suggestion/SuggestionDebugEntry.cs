using JangJang.Core;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// 편집 시 AI 대사 추천 1회 호출의 디버그 스냅샷.
/// ApiDialogueSuggestionService.OnDebugEntry 이벤트를 통해 DebugWindow에 실시간 전달된다.
/// 파일 로그(persona-debug.log)와 독립 — 창이 열려 있지 않아도 파일에는 기록된다.
/// </summary>
public sealed record SuggestionDebugEntry(
    DateTime Timestamp,
    PetState TargetState,
    string SystemPrompt,
    string UserPrompt,
    string RawResponse,
    IReadOnlyList<SuggestedLine> Parsed,
    string? ExceptionMessage);
