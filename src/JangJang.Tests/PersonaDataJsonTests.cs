using System.Text.Json;
using System.Text.Json.Serialization;
using JangJang.Core;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// PersonaData/SeedLine의 JSON 직렬화 계약을 검증한다.
/// 확장된 필드 (State, Source, PresetId 등)와 하위 호환성을 포함.
/// </summary>
public class PersonaDataJsonTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new PersonaData
        {
            Name = "테스트 최애",
            PortraitFileName = "portrait.png",
            PresetId = "tsundere",
            CustomPersonalityNotes = "화나면 말이 짧아짐",
            SeedLines = new List<SeedLine>
            {
                new() { Text = "집중하자", SituationDescription = "작업 시작할 때", State = PetState.Happy, Source = SeedLineSource.Preset, CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc) },
                new() { Text = "야 뭐해", SituationDescription = null, State = PetState.Alert, Source = SeedLineSource.UserWritten, CreatedAt = new DateTime(2026, 4, 2, 15, 30, 0, DateTimeKind.Utc) },
            }
        };

        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<PersonaData>(json, _options);

        Assert.NotNull(loaded);
        Assert.Equal(original.Name, loaded!.Name);
        Assert.Equal(original.PortraitFileName, loaded.PortraitFileName);
        Assert.Equal(original.PresetId, loaded.PresetId);
        Assert.Equal(original.CustomPersonalityNotes, loaded.CustomPersonalityNotes);
        Assert.Equal(2, loaded.SeedLines.Count);
        Assert.Equal("집중하자", loaded.SeedLines[0].Text);
        Assert.Equal(PetState.Happy, loaded.SeedLines[0].State);
        Assert.Equal(SeedLineSource.Preset, loaded.SeedLines[0].Source);
        Assert.Equal("야 뭐해", loaded.SeedLines[1].Text);
        Assert.Equal(PetState.Alert, loaded.SeedLines[1].State);
        Assert.Equal(SeedLineSource.UserWritten, loaded.SeedLines[1].Source);
    }

    [Fact]
    public void JsonRoundTrip_EmptyPersona_RoundTrips()
    {
        var original = new PersonaData();
        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<PersonaData>(json, _options);

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.Name);
        Assert.Null(loaded.PresetId);
        Assert.Null(loaded.CustomPersonalityNotes);
        Assert.Empty(loaded.SeedLines);
    }

    [Fact]
    public void BackwardCompatibility_LegacyJson_LoadsWithDefaults()
    {
        // 기존 persona.json 형식 — 이제 제거된 ToneHint/CustomToneDescription 필드가 섞여있어도
        // System.Text.Json은 모르는 필드를 조용히 무시하므로 로드가 성공해야 한다.
        var legacyJson = """
        {
          "Name": "레거시 캐릭터",
          "PortraitFileName": "portrait.png",
          "ToneHint": "반말",
          "CustomToneDescription": "퉁명스러운 반말",
          "SeedLines": [
            { "Text": "집중!", "SituationDescription": "작업 중", "CreatedAt": "2026-04-01T10:00:00Z" },
            { "Text": "뭐해", "CreatedAt": "2026-04-02T15:30:00Z" }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<PersonaData>(legacyJson, _options);

        Assert.NotNull(loaded);
        Assert.Equal("레거시 캐릭터", loaded!.Name);
        Assert.Null(loaded.PresetId);
        Assert.Equal(2, loaded.SeedLines.Count);
        // State 기본값: Happy, Source 기본값: UserWritten
        Assert.Equal(PetState.Happy, loaded.SeedLines[0].State);
        Assert.Equal(SeedLineSource.UserWritten, loaded.SeedLines[0].Source);
        Assert.Equal(PetState.Happy, loaded.SeedLines[1].State);
        Assert.Equal(SeedLineSource.UserWritten, loaded.SeedLines[1].Source);
    }

    [Fact]
    public void SeedLineSource_SerializesAsString()
    {
        var line = new SeedLine
        {
            Text = "테스트",
            State = PetState.Annoyed,
            Source = SeedLineSource.AiSuggested
        };

        var json = JsonSerializer.Serialize(line, _options);

        // enum이 숫자가 아닌 문자열로 직렬화되는지 확인
        Assert.Contains("\"AiSuggested\"", json);
        Assert.Contains("\"Annoyed\"", json);

        var loaded = JsonSerializer.Deserialize<SeedLine>(json, _options)!;
        Assert.Equal(SeedLineSource.AiSuggested, loaded.Source);
        Assert.Equal(PetState.Annoyed, loaded.State);
    }

    [Fact]
    public void SeedLineSource_AllValues_RoundTrip()
    {
        foreach (var source in Enum.GetValues<SeedLineSource>())
        {
            var line = new SeedLine { Text = "test", Source = source };
            var json = JsonSerializer.Serialize(line, _options);
            var loaded = JsonSerializer.Deserialize<SeedLine>(json, _options)!;
            Assert.Equal(source, loaded.Source);
        }
    }

    [Fact]
    public void Id_RoundTrip_Preserved()
    {
        var original = new PersonaData
        {
            Id = "abc123",
            Name = "ID 보존 테스트",
            SeedLines = new List<SeedLine> { new() { Text = "안녕" } }
        };

        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<PersonaData>(json, _options)!;

        Assert.Equal("abc123", loaded.Id);
    }

    [Fact]
    public void Id_MissingInLegacyJson_DefaultsToEmpty()
    {
        // Id 필드가 없는 레거시 JSON → Id 기본값(빈 문자열)으로 로드.
        // (PersonaStore.Load(id)가 빈 Id에 파라미터 id를 주입하는 로직은 별도)
        var legacyJson = """
        {
          "Name": "구형",
          "SeedLines": []
        }
        """;

        var loaded = JsonSerializer.Deserialize<PersonaData>(legacyJson, _options)!;
        Assert.Equal(string.Empty, loaded.Id);
    }

    [Fact]
    public void NewFieldsPresetId_RoundTrip()
    {
        var original = new PersonaData
        {
            Name = "프리셋 테스트",
            PresetId = "tsundere",
            CustomPersonalityNotes = "화나면 말이 짧아짐",
            SeedLines = new List<SeedLine>
            {
                new() { Text = "흥", State = PetState.Happy, Source = SeedLineSource.Preset }
            }
        };

        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<PersonaData>(json, _options)!;

        Assert.Equal("tsundere", loaded.PresetId);
        Assert.Equal("화나면 말이 짧아짐", loaded.CustomPersonalityNotes);
        Assert.Equal(SeedLineSource.Preset, loaded.SeedLines[0].Source);
    }
}
