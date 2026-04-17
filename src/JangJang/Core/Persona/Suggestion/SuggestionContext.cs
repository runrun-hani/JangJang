using JangJang.Core;
using JangJang.Core.Persona.Feedback;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// API 호출 시 전달되는 컨텍스트. PromptBuilder가 이를 프롬프트로 변환한다.
/// </summary>
public sealed class SuggestionContext
{
    /// <summary>프리셋의 말투/성격 설명</summary>
    public string PresetDescription { get; set; } = string.Empty;

    /// <summary>프리셋의 성격 키워드</summary>
    public List<string> PersonalityKeywords { get; set; } = new();

    /// <summary>사용자가 추가한 성격 메모 (선택)</summary>
    public string? CustomNotes { get; set; }

    /// <summary>상태별 기존 대사 목록</summary>
    public Dictionary<PetState, List<string>> ExistingLines { get; set; } = new();

    /// <summary>최근 피드백 (추천 품질 향상용)</summary>
    public List<DialogueFeedback> RecentFeedback { get; set; } = new();

    /// <summary>추천을 요청하는 상태</summary>
    public PetState TargetState { get; set; }
}

/// <summary>
/// API가 반환한 추천 대사 한 줄.
/// </summary>
public sealed class SuggestedLine
{
    /// <summary>추천된 대사 텍스트</summary>
    public string Text { get; set; } = string.Empty;
}
