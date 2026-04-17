using JangJang.Core;
using JangJang.Core.Persona.Suggestion;
using Xunit;

namespace JangJang.Tests;

public class ApiDialogueSuggestionServiceTests
{
    // --- CleanLine ---

    [Theory]
    [InlineData("뭐야, 오늘은 좀 하네.", "뭐야, 오늘은 좀 하네.")]
    [InlineData("1. 뭐야, 오늘은 좀 하네.", "뭐야, 오늘은 좀 하네.")]
    [InlineData("2) 집중해.", "집중해.")]
    [InlineData("- 열심히 해.", "열심히 해.")]
    [InlineData("* 파이팅!", "파이팅!")]
    [InlineData("10. 긴 번호도 처리", "긴 번호도 처리")]
    public void CleanLine_RemovesPrefixes(string input, string expected)
    {
        Assert.Equal(expected, ApiDialogueSuggestionService.CleanLine(input));
    }

    [Theory]
    [InlineData("\"따옴표 제거\"", "따옴표 제거")]
    [InlineData("\u201C한국어 따옴표\u201D", "한국어 따옴표")]
    public void CleanLine_RemovesQuotes(string input, string expected)
    {
        Assert.Equal(expected, ApiDialogueSuggestionService.CleanLine(input));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("a", "a")]
    public void CleanLine_EdgeCases(string input, string expected)
    {
        Assert.Equal(expected, ApiDialogueSuggestionService.CleanLine(input));
    }

    // --- ParseResponse ---

    [Fact]
    public void ParseResponse_ValidJson_ExtractsLines()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "content": "뭐야, 오늘은 좀 하네.\n...계속 그렇게 해.\n흥, 나쁘진 않아."
              }
            }
          ]
        }
        """;

        var results = ApiDialogueSuggestionService.ParseResponse(json);

        Assert.Equal(3, results.Count);
        Assert.Equal("뭐야, 오늘은 좀 하네.", results[0].Text);
        Assert.Equal("...계속 그렇게 해.", results[1].Text);
        Assert.Equal("흥, 나쁘진 않아.", results[2].Text);
    }

    [Fact]
    public void ParseResponse_NumberedOutput_CleansLines()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "content": "1. \"뭐야, 오늘은 좀 하네.\"\n2. \"집중해.\"\n3. \"흥.\""
              }
            }
          ]
        }
        """;

        var results = ApiDialogueSuggestionService.ParseResponse(json);

        Assert.Equal(3, results.Count);
        Assert.Equal("뭐야, 오늘은 좀 하네.", results[0].Text);
        Assert.Equal("집중해.", results[1].Text);
        Assert.Equal("흥.", results[2].Text);
    }

    [Fact]
    public void ParseResponse_EmptyContent_ReturnsEmpty()
    {
        var json = """
        {
          "choices": [
            {
              "message": {
                "content": ""
              }
            }
          ]
        }
        """;

        var results = ApiDialogueSuggestionService.ParseResponse(json);
        Assert.Empty(results);
    }

    [Fact]
    public void ParseResponse_InvalidJson_ReturnsEmpty()
    {
        var results = ApiDialogueSuggestionService.ParseResponse("not json");
        Assert.Empty(results);
    }

    [Fact]
    public void ParseResponse_MissingChoices_ReturnsEmpty()
    {
        var json = """{ "error": "something went wrong" }""";
        var results = ApiDialogueSuggestionService.ParseResponse(json);
        Assert.Empty(results);
    }

    // --- FromSettings ---

    [Fact]
    public void FromSettings_WithKey_ReturnsService()
    {
        var settings = new AppSettings
        {
            SuggestionApiProvider = "gemini",
            SuggestionApiKey = "test-key-123"
        };

        var service = ApiDialogueSuggestionService.FromSettings(settings);
        Assert.NotNull(service);
    }

    [Fact]
    public void FromSettings_NullKey_ReturnsNull()
    {
        var settings = new AppSettings { SuggestionApiKey = null };
        Assert.Null(ApiDialogueSuggestionService.FromSettings(settings));
    }

    [Fact]
    public void FromSettings_EmptyKey_ReturnsNull()
    {
        var settings = new AppSettings { SuggestionApiKey = "  " };
        Assert.Null(ApiDialogueSuggestionService.FromSettings(settings));
    }
}
