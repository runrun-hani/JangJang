namespace JangJang.Core.Persona;

/// <summary>
/// 페르소나의 전체 데이터. persona.json으로 직렬화된다.
/// 저장 위치: %AppData%/JangJang/Personas/{Id}/persona.json
/// 여러 페르소나를 병렬로 보관하며 AppSettings.ActivePersonaId가 현재 활성 대상을 가리킨다.
/// </summary>
public sealed class PersonaData
{
    /// <summary>
    /// 페르소나 고유 식별자. 폴더명으로 사용된다. 로드 시 비어 있으면 Store가 새 GUID를 부여한다.
    /// 사용자에게 노출되지 않으며, 이름 변경과 독립적이다.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>페르소나 이름 (사용자 표시용)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 초상화 파일명 (디렉토리 기준 상대 경로).
    /// 예: "portrait.png". 실제 파일은 current/portrait.png.
    /// 절대 경로가 아닌 이유: 페르소나 폴더 이동 시 경로가 깨지지 않도록.
    /// </summary>
    public string PortraitFileName { get; set; } = "portrait.png";

    /// <summary>선택한 프리셋 ID (예: "tsundere"). null이면 프리셋 미사용. 편집 UI에서 초기값 로드에만 사용.</summary>
    public string? PresetId { get; set; }

    /// <summary>사용자가 직접 작성하는 성격 설명. 편집 UI의 성격 메모 TextBox와 연결되며, API 프롬프트의 공식 캐릭터 성격으로 사용된다.</summary>
    public string? CustomPersonalityNotes { get; set; }

    /// <summary>씨앗 대사 풀</summary>
    public List<SeedLine> SeedLines { get; set; } = new();
}
