using JangJang.Core;
using JangJang.Core.Persona.Feedback;
using JangJang.Core.Persona.Suggestion;
using Xunit;

namespace JangJang.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void BuildUserPrompt_IncludesPresetDescription()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "겉으로는 차갑지만 속으로는 걱정하는 말투",
            PersonalityKeywords = new List<string> { "도도한", "다정한" },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("겉으로는 차갑지만 속으로는 걱정하는 말투", prompt);
        Assert.Contains("도도한", prompt);
        Assert.Contains("다정한", prompt);
        Assert.Contains("Happy", prompt);
        Assert.Contains("3개 생성", prompt);
    }

    [Fact]
    public void BuildUserPrompt_IncludesExistingLines()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "테스트",
            ExistingLines = new Dictionary<PetState, List<string>>
            {
                [PetState.Happy] = new() { "좋아!", "파이팅!" },
                [PetState.Alert] = new() { "집중해" }
            },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("좋아!", prompt);
        Assert.Contains("파이팅!", prompt);
        Assert.Contains("집중해", prompt);
    }

    [Fact]
    public void BuildUserPrompt_IncludesFeedback()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "테스트",
            RecentFeedback = new List<DialogueFeedback>
            {
                new() { OriginalText = "좋아해!", Type = FeedbackType.Accepted, State = PetState.Happy },
                new() { OriginalText = "싫어", Type = FeedbackType.Rejected, State = PetState.Happy },
                new() { OriginalText = "음", EditedText = "음...", Type = FeedbackType.Edited, State = PetState.Happy },
            },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("좋아한 대사", prompt);
        Assert.Contains("거절한 대사", prompt);
        Assert.Contains("수정한 대사", prompt);
    }

    [Fact]
    public void BuildUserPrompt_CustomNotes_Included()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "테스트",
            CustomNotes = "화나면 말이 짧아짐",
            TargetState = PetState.Annoyed
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("화나면 말이 짧아짐", prompt);
        Assert.Contains("Annoyed", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NotEmpty()
    {
        var system = PromptBuilder.BuildSystemPrompt();
        Assert.NotEmpty(system);
        Assert.Contains("캐릭터", system);
    }

    [Fact]
    public void BuildUserPrompt_EmptyContext_StillProducesPrompt()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "",
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.NotEmpty(prompt);
        Assert.Contains("Happy", prompt);
        Assert.Contains("3개 생성", prompt);
        // 기존 대사/피드백 섹션은 없어야 함
        Assert.DoesNotContain("기존 대사 예시", prompt);
        Assert.DoesNotContain("사용자 선호", prompt);
    }

    [Fact]
    public void BuildUserPrompt_FeedbackFromOtherState_Excluded()
    {
        var context = new SuggestionContext
        {
            PresetDescription = "테스트",
            RecentFeedback = new List<DialogueFeedback>
            {
                new() { OriginalText = "Alert용 대사", Type = FeedbackType.Accepted, State = PetState.Alert },
                new() { OriginalText = "Happy용 대사", Type = FeedbackType.Accepted, State = PetState.Happy },
            },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("Happy용 대사", prompt);
        Assert.DoesNotContain("Alert용 대사", prompt);
    }

    [Fact]
    public void BuildUserPrompt_AllStates_HaveDescription()
    {
        foreach (var state in Enum.GetValues<PetState>())
        {
            var context = new SuggestionContext
            {
                PresetDescription = "테스트",
                TargetState = state
            };

            var prompt = PromptBuilder.BuildUserPrompt(context, 1);
            Assert.Contains(state.ToString(), prompt);
        }
    }
}
