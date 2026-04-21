using JangJang.Core;

namespace JangJang.Core.Persona.Preset;

/// <summary>
/// 프리셋 하나의 전체 정의. 앱에 번들되는 JSON에서 역직렬화된다.
/// 프리셋 = 말투 + 성격 + 상태별 샘플 대사 세트.
/// </summary>
public sealed class PersonaPreset
{
    /// <summary>프리셋 식별자 (예: "tsundere", "supportive")</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>사용자에게 표시되는 이름 (예: "츤데레", "응원형")</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>말투/성격 설명. API 프롬프트에 포함된다.</summary>
    public string ToneDescription { get; set; } = string.Empty;

    /// <summary>성격 키워드 목록 (예: "도도한", "은근 다정한")</summary>
    public List<string> PersonalityKeywords { get; set; } = new();

    /// <summary>상태별 샘플 대사. 프리셋 선택 시 SeedLine으로 변환된다.</summary>
    public List<PresetSeedLine> SeedLines { get; set; } = new();
}

/// <summary>
/// 프리셋에 포함된 샘플 대사 한 줄.
/// 프리셋 선택 시 SeedLine으로 변환되어 PersonaData에 추가된다.
/// </summary>
public sealed class PresetSeedLine
{
    /// <summary>이 대사가 속하는 상태</summary>
    public PetState State { get; set; }

    /// <summary>대사 원문</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>상황 설명 (선택)</summary>
    public string? SituationDescription { get; set; }
}
