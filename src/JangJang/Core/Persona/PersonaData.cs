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
    /// 말투 프리셋 힌트 (선택 사항).
    /// 예: "반말", "존댓말", 자유 기술 문자열, 또는 null.
    /// MVP에서 실제 활용 여부는 Step 6 시작 시 재평가 (설계 문서 열린 질문 4번).
    /// </summary>
    public string? ToneHint { get; set; }

    /// <summary>씨앗 대사 풀</summary>
    public List<SeedLine> SeedLines { get; set; } = new();
}
