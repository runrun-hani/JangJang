using System.IO;
using System.Text.Json;
using JangJang.Core;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// PersonaStore의 다중 페르소나 API 계약 검증.
/// PersonasRoot 고정 경로를 사용하므로 각 테스트는 고유한 GUID를 써서 격리한다.
/// 테스트 종료 후 해당 폴더만 정리 — 다른 페르소나에 영향 없음.
/// </summary>
public class PersonaStoreMultiTests : IDisposable
{
    private readonly List<string> _tempIds = new();

    private string NewTempId()
    {
        var id = "test-" + Guid.NewGuid().ToString("N");
        _tempIds.Add(id);
        return id;
    }

    public void Dispose()
    {
        foreach (var id in _tempIds)
        {
            try
            {
                var dir = PersonaStore.GetPersonaDir(id);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesIdAndName()
    {
        var id = NewTempId();
        var original = new PersonaData
        {
            Id = id,
            Name = "다중 저장소 테스트",
            SeedLines = new List<SeedLine> { new() { Text = "안녕", State = PetState.Happy } }
        };

        PersonaStore.Save(original);
        var loaded = PersonaStore.Load(id);

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
        Assert.Equal("다중 저장소 테스트", loaded.Name);
        Assert.Single(loaded.SeedLines);
    }

    [Fact]
    public void Save_EmptyId_AssignsNewGuid()
    {
        var original = new PersonaData { Name = "Id 자동 발급" };
        Assert.Equal(string.Empty, original.Id);

        PersonaStore.Save(original);
        _tempIds.Add(original.Id); // cleanup 대상에 등록

        Assert.False(string.IsNullOrEmpty(original.Id));
        var loaded = PersonaStore.Load(original.Id);
        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded!.Id);
    }

    [Fact]
    public void Load_EmptyIdInJson_InjectsParameterId()
    {
        // 수동으로 Id 없는 persona.json을 쓰고 Load가 파라미터 Id를 주입하는지 검증.
        var id = NewTempId();
        var dir = PersonaStore.GetPersonaDir(id);
        Directory.CreateDirectory(dir);
        var legacyJson = """
        {
          "Name": "Id 없는 레거시",
          "SeedLines": []
        }
        """;
        File.WriteAllText(Path.Combine(dir, "persona.json"), legacyJson);

        var loaded = PersonaStore.Load(id);
        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
        Assert.Equal("Id 없는 레거시", loaded.Name);
    }

    [Fact]
    public void Load_NonexistentId_ReturnsNull()
    {
        var id = "nonexistent-" + Guid.NewGuid().ToString("N");
        var loaded = PersonaStore.Load(id);
        Assert.Null(loaded);
    }

    [Fact]
    public void Load_NullOrEmptyId_ReturnsNull()
    {
        Assert.Null(PersonaStore.Load(string.Empty));
        Assert.Null(PersonaStore.Load(null!));
    }

    [Fact]
    public void Exists_NonexistentId_ReturnsFalse()
    {
        Assert.False(PersonaStore.Exists(string.Empty));
        Assert.False(PersonaStore.Exists("no-such-id"));
    }

    [Fact]
    public void GetPortraitFullPath_UsesPersonaSpecificDir()
    {
        var id = NewTempId();
        var data = new PersonaData { Id = id, PortraitFileName = "portrait.jpg" };
        var path = PersonaStore.GetPortraitFullPath(data);
        Assert.Contains(id, path);
        Assert.EndsWith("portrait.jpg", path);
    }

    [Fact]
    public void EnumerateAllPersonaIdsOnDisk_FindsSavedPersona()
    {
        var id = NewTempId();
        PersonaStore.Save(new PersonaData { Id = id, Name = "enum test" });

        var ids = PersonaStore.EnumerateAllPersonaIdsOnDisk().ToList();
        Assert.Contains(id, ids);
    }

    [Fact]
    public void EnumerateAllPersonaIdsOnDisk_ExcludesLegacyCurrent()
    {
        // current/ 폴더는 마이그레이션 대상이므로 목록 스캔에서 제외.
        var ids = PersonaStore.EnumerateAllPersonaIdsOnDisk().ToList();
        Assert.DoesNotContain("current", ids);
    }
}
