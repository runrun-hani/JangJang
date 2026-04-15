namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// 대사 생성 파이프라인 1단계 — 상황 수집.
/// PetViewModel이 호출하는 기본 컨텍스트(state/annoyance/todaySeconds)를 받아
/// ActivityMonitor 등 추가 정보 소스에서 더 풍부한 컨텍스트를 만들어 반환한다.
/// </summary>
public interface IContextCollector
{
    DialogueContext Collect(DialogueContext basicContext);
}
