namespace JangJang.Core.Persona;

/// <summary>
/// 현재 활성 페르소나의 전체 데이터. persona.json으로 직렬화된다.
/// 저장 위치: %AppData%/JangJang/Personas/current/persona.json
/// 단일 페르소나 구조지만 "current" 폴더 구조를 유지하여 나중 다중 전환 시 확장 여지를 남김.
/// </summary>
public sealed class PersonaData
{
    /// <summary>페르소나 이름 (사용자 표시용)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 초상화 파일명 (디렉토리 기준 상대 경로).
    /// 예: "portrait.png". 실제 파일은 current/portrait.png.
    /// 절대 경로가 아닌 이유: 페르소나 폴더 이동 시 경로가 깨지지 않도록.
    /// </summary>
    public string PortraitFileName { get; set; } = "portrait.png";

    /// <summary>
    /// [레거시] 말투 프리셋 힌트. 기존 persona.json 하위 호환용.
    /// 로드 시 CustomToneDescription이 비어있으면 이 값으로 채운다.
    /// 새 저장 시에는 CustomToneDescription을 사용한다.
    /// </summary>
    public string? ToneHint { get; set; }

    /// <summary>선택한 프리셋 ID (예: "tsundere"). null이면 프리셋 미사용.</summary>
    public string? PresetId { get; set; }

    /// <summary>사용자 정의 말투 설명. 프리셋의 ToneDescription 위에 덮어쓴다.</summary>
    public string? CustomToneDescription { get; set; }

    /// <summary>사용자가 추가한 성격 메모. API 프롬프트에 포함된다.</summary>
    public string? CustomPersonalityNotes { get; set; }

    /// <summary>씨앗 대사 풀</summary>
    public List<SeedLine> SeedLines { get; set; } = new();
}
