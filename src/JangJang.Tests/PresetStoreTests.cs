using System.Text.Json;
using JangJang.Core;
using JangJang.Core.Persona.Preset;
using Xunit;

namespace JangJang.Tests;

public class PresetStoreTests
{
    [Fact]
    public void LoadAll_ReturnsAtLeastThreePresets()
    {
        var presets = PresetStore.LoadAll();
        Assert.True(presets.Count >= 3, $"Expected at least 3 presets, got {presets.Count}");
    }

    [Theory]
    [InlineData("tsundere", "츤데레")]
    [InlineData("supportive", "응원형")]
    [InlineData("calm", "차분한")]
    public void LoadById_ReturnsExpectedPreset(string id, string expectedName)
    {
        var preset = PresetStore.LoadById(id);
        Assert.NotNull(preset);
        Assert.Equal(id, preset!.Id);
        Assert.Equal(expectedName, preset.DisplayName);
        Assert.NotEmpty(preset.ToneDescription);
        Assert.NotEmpty(preset.PersonalityKeywords);
        Assert.NotEmpty(preset.SeedLines);
    }

    [Fact]
    public void LoadById_UnknownId_ReturnsNull()
    {
        var preset = PresetStore.LoadById("nonexistent");
        Assert.Null(preset);
    }

    [Theory]
    [InlineData("tsundere")]
    [InlineData("supportive")]
    [InlineData("calm")]
    public void Preset_HasSeedLinesForAllStates(string id)
    {
        var preset = PresetStore.LoadById(id)!;
        var states = Enum.GetValues<PetState>();

        foreach (var state in states)
        {
            var linesForState = preset.SeedLines.Where(s => s.State == state).ToList();
            Assert.True(linesForState.Count >= 2,
                $"Preset '{id}' should have at least 2 lines for {state}, got {linesForState.Count}");
        }
    }

    [Fact]
    public void PresetSeedLine_JsonRoundTrip()
    {
        var original = new PersonaPreset
        {
            Id = "test",
            DisplayName = "테스트",
            ToneDescription = "테스트 말투",
            PersonalityKeywords = new List<string> { "키워드1", "키워드2" },
            SeedLines = new List<PresetSeedLine>
            {
                new() { State = PetState.Happy, Text = "좋아!", SituationDescription = "작업 중" },
                new() { State = PetState.Annoyed, Text = "뭐해.", SituationDescription = null },
            }
        };

        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(original, options);
        var loaded = JsonSerializer.Deserialize<PersonaPreset>(json, options);

        Assert.NotNull(loaded);
        Assert.Equal("test", loaded!.Id);
        Assert.Equal(2, loaded.SeedLines.Count);
        Assert.Equal(PetState.Happy, loaded.SeedLines[0].State);
        Assert.Equal("좋아!", loaded.SeedLines[0].Text);
        Assert.Equal(PetState.Annoyed, loaded.SeedLines[1].State);
        Assert.Null(loaded.SeedLines[1].SituationDescription);
    }
}
