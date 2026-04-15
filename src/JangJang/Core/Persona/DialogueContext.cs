namespace JangJang.Core.Persona;

/// <summary>
/// 대사 생성 요청 시 전달되는 상황 스냅샷.
/// Step 1 범위에서는 기존 Dialogue.GetLine 파라미터만 담는다.
/// Step 4에서 SessionSeconds, IdleSessionSeconds, TimeOfDay 등 상황 변수를 확장한다.
/// </summary>
public sealed class DialogueContext
{
    public PetState State { get; init; }
    public double Annoyance { get; init; }
    public int TodaySeconds { get; init; }
}
