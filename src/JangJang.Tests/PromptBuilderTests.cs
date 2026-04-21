using JangJang.Core;
using JangJang.Core.Persona.Feedback;
using JangJang.Core.Persona.Suggestion;
using Xunit;

namespace JangJang.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void BuildUserPrompt_IncludesExistingLines()
    {
        var context = new SuggestionContext
        {
            ExistingLines = new Dictionary<PetState, List<string>>
            {
                [PetState.Happy] = new() { "좋아!", "파이팅!" },
                [PetState.Alert] = new() { "집중해" }
            },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        // TargetState(Happy) 대사는 "중복 금지"에 들어가고, 다른 상태(Alert)는 "말투 참고"에 들어간다.
        Assert.Contains("좋아!", prompt);
        Assert.Contains("파이팅!", prompt);
        Assert.Contains("집중해", prompt);
    }

    [Fact]
    public void BuildUserPrompt_IncludesFeedback()
    {
        var context = new SuggestionContext
        {
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
            CustomNotes = "화나면 말이 짧아짐",
            TargetState = PetState.Annoyed
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("화나면 말이 짧아짐", prompt);
        Assert.Contains("캐릭터 성격", prompt);
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
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.NotEmpty(prompt);
        Assert.Contains("Happy", prompt);
        Assert.Contains("3개 생성", prompt);
        // 성격/기존 대사/피드백 섹션은 없어야 함
        Assert.DoesNotContain("캐릭터 성격", prompt);
        Assert.DoesNotContain("기존 대사 예시", prompt);
        Assert.DoesNotContain("사용자 선호", prompt);
    }

    [Fact]
    public void BuildUserPrompt_FeedbackFromOtherState_Excluded()
    {
        var context = new SuggestionContext
        {
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
                TargetState = state
            };

            var prompt = PromptBuilder.BuildUserPrompt(context, 1);
            Assert.Contains(state.ToString(), prompt);
        }
    }

    [Fact]
    public void BuildUserPrompt_TargetStateLines_GoToDuplicateBlock_NotExample()
    {
        // TargetState의 기존 대사는 "중복 금지" 섹션에 들어가야 하고,
        // 다른 상태의 대사만 "말투 참고" 섹션에 들어가야 한다 (모방 편향 방지).
        var context = new SuggestionContext
        {
            ExistingLines = new Dictionary<PetState, List<string>>
            {
                [PetState.Happy] = new() { "즐거워!" },
                [PetState.Alert] = new() { "집중!" }
            },
            TargetState = PetState.Happy
        };

        var prompt = PromptBuilder.BuildUserPrompt(context, 3);

        Assert.Contains("중복 금지", prompt);
        var dupIdx = prompt.IndexOf("중복 금지", StringComparison.Ordinal);
        var happyIdx = prompt.IndexOf("즐거워!", StringComparison.Ordinal);
        var alertIdx = prompt.IndexOf("집중!", StringComparison.Ordinal);

        // "즐거워!"는 중복 금지 이후에 나와야 한다 (예시 섹션에는 포함되지 않음)
        Assert.True(happyIdx > dupIdx, "TargetState 대사는 중복 금지 섹션에 있어야 함");
        // "집중!"(Alert)은 중복 금지 앞 예시 섹션에 나와야 한다
        Assert.True(alertIdx < dupIdx, "다른 상태 대사는 예시 섹션에 있어야 함");
    }
}
