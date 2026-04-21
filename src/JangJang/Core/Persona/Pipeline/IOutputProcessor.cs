namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// 대사 생성 파이프라인 3단계 — 후보 가공/선택.
/// (가) MVP: PassthroughOutputProcessor — 후보 중 가중치 랜덤 선택만.
/// (나) 확장: LlmVariationOutputProcessor — 선택된 후보를 LLM으로 살짝 변주.
/// 둘은 동일 인터페이스를 통해 교체 가능.
/// </summary>
public interface IOutputProcessor
{
    /// <summary>
    /// 후보 리스트 중 최종 한 줄을 반환.
    /// 후보가 비었으면 빈 문자열 또는 폴백 문자열.
    /// </summary>
    string Process(IReadOnlyList<SeedLine> candidates, DialogueContext context);
}
