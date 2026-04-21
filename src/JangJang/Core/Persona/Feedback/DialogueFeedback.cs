using JangJang.Core;

namespace JangJang.Core.Persona.Feedback;

public enum FeedbackType
{
    Accepted,
    Edited,
    Rejected
}

/// <summary>
/// AI 추천 대사에 대한 사용자 피드백 한 건.
/// 수락/편집/거절 기록을 누적하여 다음 추천의 컨텍스트로 활용된다.
/// </summary>
public sealed class DialogueFeedback
{
    /// <summary>API가 제안한 원본 대사</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>사용자가 편집한 경우의 최종 텍스트. Accepted/Rejected면 null.</summary>
    public string? EditedText { get; set; }

    /// <summary>피드백 유형</summary>
    public FeedbackType Type { get; set; }

    /// <summary>어떤 상태에서의 추천이었는지</summary>
    public PetState State { get; set; }

    /// <summary>피드백 시각</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
