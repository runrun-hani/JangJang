using System.Text;
using JangJang.Core;
using JangJang.Core.Persona.Feedback;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// SuggestionContext를 API 호출용 프롬프트(시스템 + 유저 메시지)로 변환한다.
/// </summary>
public static class PromptBuilder
{
    private static readonly Dictionary<PetState, string> StateDescriptions = new()
    {
        [PetState.Happy] = "사용자가 열심히 작업에 집중하고 있는 상황",
        [PetState.Alert] = "사용자가 잠깐 한눈을 팔거나 딴짓을 하는 상황",
        [PetState.Annoyed] = "사용자가 작업을 오래 안 하고 있어서 답답한 상황",
        [PetState.Sleeping] = "사용자가 아직 작업을 시작하지 않았거나 자리를 비운 상황",
        [PetState.WakeUp] = "사용자가 막 돌아와서 작업을 다시 시작하려는 상황",
    };

    public static string BuildSystemPrompt()
    {
        return "당신은 캐릭터 대사 작가입니다. 주어진 캐릭터 프로필에 맞는 짧은 대사를 생성합니다. 대사만 출력하세요.";
    }

    public static string BuildUserPrompt(SuggestionContext context, int count)
    {
        var sb = new StringBuilder();

        // 1. 캐릭터 프로필
        sb.AppendLine("## 캐릭터 프로필");
        sb.AppendLine(context.PresetDescription);
        if (context.PersonalityKeywords.Count > 0)
            sb.AppendLine($"성격 키워드: {string.Join(", ", context.PersonalityKeywords)}");
        if (!string.IsNullOrWhiteSpace(context.CustomToneDescription)
            && !string.Equals(context.CustomToneDescription.Trim(), context.PresetDescription?.Trim(), StringComparison.Ordinal))
        {
            sb.AppendLine($"사용자 정의 말투: {context.CustomToneDescription.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(context.CustomNotes))
            sb.AppendLine($"추가 메모: {context.CustomNotes}");
        sb.AppendLine();

        // 2. 기존 대사 예시
        if (context.ExistingLines.Count > 0)
        {
            sb.AppendLine("## 기존 대사 예시 (이 캐릭터의 말투 참고)");
            foreach (var (state, lines) in context.ExistingLines)
            {
                if (lines.Count == 0) continue;
                sb.AppendLine($"[{state}]");
                foreach (var line in lines.Take(5))
                    sb.AppendLine($"  - \"{line}\"");
            }
            sb.AppendLine();
        }

        // 3. 피드백 반영
        var accepted = context.RecentFeedback
            .Where(f => f.Type == FeedbackType.Accepted && f.State == context.TargetState)
            .Select(f => f.OriginalText)
            .TakeLast(3)
            .ToList();
        var edited = context.RecentFeedback
            .Where(f => f.Type == FeedbackType.Edited && f.State == context.TargetState)
            .Select(f => $"\"{f.OriginalText}\" → \"{f.EditedText}\"")
            .TakeLast(3)
            .ToList();
        var rejected = context.RecentFeedback
            .Where(f => f.Type == FeedbackType.Rejected && f.State == context.TargetState)
            .Select(f => f.OriginalText)
            .TakeLast(3)
            .ToList();

        if (accepted.Count > 0 || edited.Count > 0 || rejected.Count > 0)
        {
            sb.AppendLine("## 사용자 선호");
            if (accepted.Count > 0)
                sb.AppendLine($"좋아한 대사: {string.Join(", ", accepted.Select(a => $"\"{a}\""))}");
            if (edited.Count > 0)
                sb.AppendLine($"수정한 대사: {string.Join(", ", edited)}");
            if (rejected.Count > 0)
                sb.AppendLine($"거절한 대사: {string.Join(", ", rejected.Select(r => $"\"{r}\""))}");
            sb.AppendLine();
        }

        // 4. 요청
        var stateDesc = StateDescriptions.GetValueOrDefault(context.TargetState, context.TargetState.ToString());
        sb.AppendLine($"## 요청");
        sb.AppendLine($"상태: {context.TargetState} — {stateDesc}");
        sb.AppendLine($"위 캐릭터가 이 상태에서 할 법한 대사를 {count}개 생성하세요.");
        sb.AppendLine($"각 대사는 한 줄, 20자 이내. 번호 없이 대사만 한 줄씩 출력하세요.");
        sb.AppendLine($"기존 대사와 동일하거나 거의 같은 대사는 생성하지 마세요. 새로운 표현을 사용하세요.");

        return sb.ToString();
    }
}
