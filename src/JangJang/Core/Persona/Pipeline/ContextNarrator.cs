using System.Text;

namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// DialogueContext를 한국어 자연어 문장으로 변환한다.
/// 이 문장이 EmbeddingCandidateSelector의 "쿼리 임베딩" 입력이 되어
/// 사용자가 쓴 씨앗 대사(또는 그 상황 설명)와 의미 매칭된다.
///
/// 규칙 기반 (LLM 없음). 상태/시간/세션 길이/분노도를 자연스러운 한국어 구로 조합.
/// </summary>
public sealed class ContextNarrator
{
    public string Narrate(DialogueContext ctx)
    {
        var parts = new List<string>();

        // 1. 상태 표현
        switch (ctx.State)
        {
            case PetState.Happy:
                parts.Add("열심히 작업에 집중하고 있다");
                break;
            case PetState.Alert:
                parts.Add("잠깐 작업에서 한눈을 팔았다");
                break;
            case PetState.Annoyed:
                if (ctx.Annoyance < 0.5)
                    parts.Add("작업을 내려놓고 다른 일을 하고 있다");
                else
                    parts.Add("작업을 한참 동안 안 하고 있어 보고 있기 답답하다");
                break;
            case PetState.Sleeping:
                parts.Add("아직 작업을 시작하지 않았거나 자리를 비운 상태이다");
                break;
            case PetState.WakeUp:
                parts.Add("막 작업을 다시 시작하려는 참이다");
                break;
        }

        // 2. 세션 작업 시간
        if (ctx.SessionSeconds > 0)
        {
            var hours = ctx.SessionSeconds / 3600;
            var minutes = (ctx.SessionSeconds % 3600) / 60;
            if (hours >= 3)
                parts.Add($"이번 세션에 {hours}시간 넘게 일했다");
            else if (hours >= 1)
                parts.Add($"이번 세션에 약 {hours}시간 일했다");
            else if (minutes >= 30)
                parts.Add("막 어느 정도 작업을 한 참이다");
            else
                parts.Add("이제 막 작업을 시작했다");
        }

        // 3. 오늘 누적 작업 시간
        if (ctx.TodaySeconds >= 4 * 3600)
            parts.Add("오늘 이미 많은 작업을 했다");
        else if (ctx.TodaySeconds >= 2 * 3600)
            parts.Add("오늘 어느 정도 작업을 했다");

        // 4. 시간대
        var hour = ctx.Timestamp.Hour;
        if (hour >= 5 && hour < 9)
            parts.Add("이른 아침 시간이다");
        else if (hour >= 9 && hour < 12)
            parts.Add("오전 시간이다");
        else if (hour >= 12 && hour < 14)
            parts.Add("점심 무렵이다");
        else if (hour >= 14 && hour < 18)
            parts.Add("오후 시간이다");
        else if (hour >= 18 && hour < 22)
            parts.Add("저녁 시간이다");
        else
            parts.Add("밤늦은 시간이다");

        // 결합
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i]);
            if (i < parts.Count - 1) sb.Append(". ");
            else sb.Append(".");
        }
        return sb.ToString();
    }
}
