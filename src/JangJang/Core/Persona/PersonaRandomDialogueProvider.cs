using JangJang.Core.Persona.Pipeline;

namespace JangJang.Core.Persona;

/// <summary>
/// 페르소나의 작성 대사 풀에서 PetState로 필터링 후 가중치 랜덤으로 한 줄 선택.
/// 임베딩 모델 없이 동작하는 경로 — 앱 시작 시 Onnx/Tokenizer를 로드하지 않는다.
///
/// 해당 상태 대사가 0개면 빈 문자열 반환 → Dialogue.cs의 폴백이
/// DefaultDialogueProvider(치와와)로 대체한다.
///
/// 선택 시점:
///   - PersonaEnabled=true && (EmbeddingMatchingEnabled=false || 모델 미설치)
///   - PersonaDialogueProvider 초기화 실패 시 폴백
/// </summary>
public sealed class PersonaRandomDialogueProvider : IDialogueProvider
{
    private readonly PersonaData _persona;
    private readonly PassthroughOutputProcessor _processor = new();

    public PersonaRandomDialogueProvider(PersonaData persona)
    {
        _persona = persona;
    }

    public string GetLine(DialogueContext context)
    {
        var candidates = _persona.SeedLines
            .Where(l => l.State == context.State)
            .ToList();

        if (candidates.Count == 0) return string.Empty;
        return _processor.Process(candidates, context);
    }
}
