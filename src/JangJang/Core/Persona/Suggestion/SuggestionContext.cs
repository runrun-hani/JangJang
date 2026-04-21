using JangJang.Core;
using JangJang.Core.Persona.Feedback;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// API 호출 시 전달되는 컨텍스트. PromptBuilder가 이를 프롬프트로 변환한다.
/// </summary>
public sealed class SuggestionContext
{
    /// <summary>
    /// 사용자가 직접 작성한 캐릭터 성격 설명. 프롬프트의 "공식 성격" 섹션으로 들어간다.
    /// 편집 UI의 성격 메모 TextBox와 연결된 PersonaData.CustomPersonalityNotes와 동일한 값.
    /// </summary>
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

    /// <summary>
    /// 추천과 함께 LLM이 생성한 상황 설명. 없거나 파싱 실패 시 null.
    /// 사용자가 수락하면 SeedLine.SituationDescription으로 저장되어
    /// 런타임 매칭 시 B 모드(설명 기반 임베딩) 키가 된다.
    /// </summary>
    public string? SituationDescription { get; set; }
}
