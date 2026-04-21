namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// ActivityMonitor의 현재 상태를 읽어 DialogueContext를 보강하는 기본 구현.
/// 호출 측(Dialogue.GetLine 위임)에서 받은 기본 3개 필드를 유지하면서
/// 세션 정보와 타임스탬프를 덧붙인다.
/// </summary>
public sealed class DefaultContextCollector : IContextCollector
{
    private readonly ActivityMonitor _monitor;

    public DefaultContextCollector(ActivityMonitor monitor)
    {
        _monitor = monitor;
    }

    public DialogueContext Collect(DialogueContext basicContext)
    {
        return new DialogueContext
        {
            State = basicContext.State,
            Annoyance = basicContext.Annoyance,
            TodaySeconds = basicContext.TodaySeconds,
            SessionSeconds = _monitor.SessionSeconds,
            IdleSessionSeconds = _monitor.IdleSessionSeconds,
            Timestamp = DateTime.Now
        };
    }
}
