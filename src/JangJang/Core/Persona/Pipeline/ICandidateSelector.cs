namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// 대사 생성 파이프라인 2단계 — 후보 선정.
/// 상황 서술문(narration) 및 컨텍스트를 받아 씨앗 대사 풀에서 상위 N개 후보를 반환한다.
/// </summary>
public interface ICandidateSelector
{
    /// <summary>
    /// 후보 N개를 유사도 내림차순으로 반환. 풀이 비었으면 빈 리스트.
    /// </summary>
    IReadOnlyList<SeedLine> Select(string narration, DialogueContext context, int topN);
}
