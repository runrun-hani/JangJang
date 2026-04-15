using System.Text.Json;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// PersonaStore는 정적 %AppData% 경로를 사용해서 테스트 격리가 어렵다
/// (사용자 실제 페르소나 파일을 덮어쓸 위험). 대신 PersonaData 클래스 자체의
/// JSON 직렬화/역직렬화 round-trip을 테스트하여 데이터 계약을 검증한다.
///
/// PersonaStore.Save/Load는 System.Text.Json을 감싸고 있으므로,
/// 데이터 계약이 안정적이면 I/O 레이어도 안정적이다.
/// </summary>
public class PersonaDataJsonTests
{
    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new PersonaData
        {
            Name = "테스트 최애",
            PortraitFileName = "portrait.png",
            ToneHint = "반말",
            SeedLines = new List<SeedLine>
            {
                new() { Text = "집중하자", SituationDescription = "작업 시작할 때", CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc) },
                new() { Text = "야 뭐해", SituationDescription = null, CreatedAt = new DateTime(2026, 4, 2, 15, 30, 0, DateTimeKind.Utc) },
            }
        };

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var loaded = JsonSerializer.Deserialize<PersonaData>(json);

        Assert.NotNull(loaded);
        Assert.Equal(original.Name, loaded!.Name);
        Assert.Equal(original.PortraitFileName, loaded.PortraitFileName);
        Assert.Equal(original.ToneHint, loaded.ToneHint);
        Assert.Equal(2, loaded.SeedLines.Count);
        Assert.Equal("집중하자", loaded.SeedLines[0].Text);
        Assert.Equal("작업 시작할 때", loaded.SeedLines[0].SituationDescription);
        Assert.Equal("야 뭐해", loaded.SeedLines[1].Text);
        Assert.Null(loaded.SeedLines[1].SituationDescription);
    }

    [Fact]
    public void JsonRoundTrip_EmptyPersona_RoundTrips()
    {
        var original = new PersonaData();
        var json = JsonSerializer.Serialize(original);
        var loaded = JsonSerializer.Deserialize<PersonaData>(json);

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.Name);
        Assert.Null(loaded.ToneHint);
        Assert.Empty(loaded.SeedLines);
    }
}
