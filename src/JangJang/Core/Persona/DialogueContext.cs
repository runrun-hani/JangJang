namespace JangJang.Core.Persona;

/// <summary>
/// 대사 생성 요청 시 전달되는 상황 스냅샷.
/// 매칭에 사용할 변수들을 모두 담는다 — ContextNarrator가 이 정보로 한국어 문장을 만든다.
/// </summary>
public sealed class DialogueContext
{
    /// <summary>현재 펫 상태 (Happy/Alert/Annoyed/Sleeping/WakeUp)</summary>
    public PetState State { get; init; }

    /// <summary>분노도 (0.0 ~ 1.0). Annoyed 상태에서만 의미 있음.</summary>
    public double Annoyance { get; init; }

    /// <summary>오늘 누적 작업시간 (초). WorkLog.TodaySeconds.</summary>
    public int TodaySeconds { get; init; }

    /// <summary>현재 세션 누적 작업시간 (초). ActivityMonitor.SessionSeconds.</summary>
    public int SessionSeconds { get; init; }

    /// <summary>현재 세션 누적 유휴시간 (초). ActivityMonitor.IdleSessionSeconds.</summary>
    public int IdleSessionSeconds { get; init; }

    /// <summary>스냅샷 생성 시각 (로컬). 시간대 매칭에 사용.</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
