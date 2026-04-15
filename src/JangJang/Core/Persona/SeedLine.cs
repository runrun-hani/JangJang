namespace JangJang.Core.Persona;

/// <summary>
/// 씨앗 대사 한 줄. 사용자가 직접 작성한 원본 대사.
/// SituationDescription은 선택 사항:
///   - 있으면: 해당 설명 벡터로 상황 매칭 (B 모드)
///   - 없으면: 대사 본문 벡터로 매칭 (C 모드, EmbeddingCandidateSelector가 자동 전환)
/// </summary>
public sealed class SeedLine
{
    /// <summary>대사 원문. 사용자가 직접 작성한 그대로.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 이 대사가 어울리는 상황에 대한 사용자의 설명 (선택 사항).
    /// 예: "오래 집중하다 지쳤을 때", "막 시작할 때".
    /// 비어있으면 대사 본문 벡터로 매칭 (C 모드).
    /// </summary>
    public string? SituationDescription { get; set; }

    /// <summary>이 씨앗 대사가 추가된 시각. 정렬/히스토리용.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
