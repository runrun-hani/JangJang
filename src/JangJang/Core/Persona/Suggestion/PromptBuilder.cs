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

        // 1. 캐릭터 정의
        //    사용자가 직접 작성한 CustomNotes가 유일한 공식 성격 입력. 프리셋은 편집 UI에서
        //    대사/이름 초기값을 채우는 힌트일 뿐이므로 프롬프트에는 포함하지 않는다.
        if (!string.IsNullOrWhiteSpace(context.CustomNotes))
        {
            sb.AppendLine("## 캐릭터 성격 (공식 — 반드시 이 설명을 중심으로 따를 것)");
            sb.AppendLine(context.CustomNotes.Trim());
            sb.AppendLine();
        }

        // 2. 기존 대사 예시 — TargetState는 제외 (모방 편향 방지).
        //    다른 상태 대사만 보여주어 "말투"를 참고하되, 현재 생성할 상태의
        //    기존 대사는 "## 중복 금지"로 따로 전달한다.
        var otherStateLines = context.ExistingLines
            .Where(kv => kv.Key != context.TargetState && kv.Value.Count > 0)
            .ToList();
        if (otherStateLines.Count > 0)
        {
            sb.AppendLine("## 기존 대사 예시 (이 캐릭터의 말투 참고)");
            foreach (var (state, lines) in otherStateLines)
            {
                sb.AppendLine($"[{state}]");
                foreach (var line in lines.Take(5))
                    sb.AppendLine($"  - \"{line}\"");
            }
            sb.AppendLine();
        }

        // 2-1. 중복 금지 — TargetState의 기존 대사만. "예시"가 아니라 "피해야 할 목록".
        if (context.ExistingLines.TryGetValue(context.TargetState, out var existingInTarget)
            && existingInTarget.Count > 0)
        {
            sb.AppendLine("## 중복 금지 (아래 대사와 같거나 거의 같은 표현은 만들지 말 것)");
            foreach (var line in existingInTarget.Take(10))
                sb.AppendLine($"  - \"{line}\"");
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
        sb.AppendLine($"각 대사는 20자 이내.");
        sb.AppendLine($"기존 대사와 동일하거나 거의 같은 대사는 생성하지 마세요. 새로운 표현을 사용하세요.");
        sb.AppendLine();
        sb.AppendLine($"## 출력 형식");
        sb.AppendLine($"각 추천은 다음 형식으로 한 줄씩 출력하세요 (번호·글머리·따옴표 없이).");
        sb.AppendLine($"대사 | 상황 설명");
        sb.AppendLine();
        sb.AppendLine($"상황 설명은 이 대사가 자연스럽게 나올 법한 사용자의 작업 맥락을 30자 이내의 평서문으로 적습니다.");
        sb.AppendLine($"사용자의 작업 흐름(막 시작했다 / 오래 집중했다 / 자리를 비웠다 / 딴짓을 한다 등)을 구체적으로 서술하세요.");
        sb.AppendLine($"예시:");
        sb.AppendLine($"흥, 좀 하네. | 막 작업을 시작해 집중이 붙기 시작한 상황");
        sb.AppendLine($"언제까지 그럴 거야? | 작업을 한참 안 하고 있어 답답한 상황");

        return sb.ToString();
    }
}
