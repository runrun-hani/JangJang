using System.Text.Json;
using System.Text.Json.Serialization;
using JangJang.Core;
using JangJang.Core.Persona.Feedback;
using Xunit;

namespace JangJang.Tests;

public class FeedbackDataTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new List<DialogueFeedback>
        {
            new()
            {
                OriginalText = "뭐야, 오늘은 좀 하네.",
                EditedText = null,
                Type = FeedbackType.Accepted,
                State = PetState.Happy,
                Timestamp = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                OriginalText = "열심히 해.",
                EditedText = "열심히 해봐.",
                Type = FeedbackType.Edited,
                State = PetState.Happy,
                Timestamp = new DateTime(2026, 4, 16, 10, 5, 0, DateTimeKind.Utc)
            },
            new()
            {
                OriginalText = "잘하고 있어!",
                EditedText = null,
                Type = FeedbackType.Rejected,
                State = PetState.Happy,
                Timestamp = new DateTime(2026, 4, 16, 10, 10, 0, DateTimeKind.Utc)
            }
        };

        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<List<DialogueFeedback>>(json, _options);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Count);

        Assert.Equal(FeedbackType.Accepted, loaded[0].Type);
        Assert.Null(loaded[0].EditedText);

        Assert.Equal(FeedbackType.Edited, loaded[1].Type);
        Assert.Equal("열심히 해봐.", loaded[1].EditedText);

        Assert.Equal(FeedbackType.Rejected, loaded[2].Type);
        Assert.Equal(PetState.Happy, loaded[2].State);
    }

    [Fact]
    public void EmptyList_RoundTrips()
    {
        var original = new List<DialogueFeedback>();
        var json = JsonSerializer.Serialize(original, _options);
        var loaded = JsonSerializer.Deserialize<List<DialogueFeedback>>(json, _options);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!);
    }
}
