namespace JangJang.Core.Persona;

/// <summary>
/// 현재 상황에 맞는 대사 한 줄을 반환하는 Provider.
/// 구현체:
///   - DefaultDialogueProvider: 기존 치와와 하드코딩 대사 (폴백)
///   - PersonaDialogueProvider: 사용자 페르소나 기반 (Step 4에서 구현)
/// </summary>
public interface IDialogueProvider
{
    string GetLine(DialogueContext context);
}
