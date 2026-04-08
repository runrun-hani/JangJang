using System.Diagnostics;
using System.Windows.Threading;
using JangJang.Interop;

namespace JangJang.Core;

public class ActivityMonitor : IDisposable
{
    private readonly AppSettings _settings;
    private readonly WorkLog _workLog;
    private readonly DispatcherTimer _timer;
    private int _tickCount;
    private int _saveCounter;
    private int _sessionSeconds;
    private bool _targetRunning;
    private HashSet<uint> _targetPids = new();
    private double _idleSeconds;

    public PetState CurrentState { get; private set; } = PetState.Sleeping;
    public double AnnoyanceLevel { get; private set; }
    public WorkLog WorkLog => _workLog;
    public int SessionSeconds => _sessionSeconds;

    public event Action<PetState, double>? StateUpdated;

    public ActivityMonitor(AppSettings settings)
    {
        _settings = settings;
        _workLog = WorkLog.Load();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        _tickCount++;

        if (_tickCount == 1 || _tickCount % 5 == 0)
            RefreshTargetProcesses();

        if (!_targetRunning)
        {
            _idleSeconds = 0;
            UpdateState(PetState.Sleeping, 0);
            return;
        }

        var fgPid = NativeMethods.GetForegroundProcessId();
        bool targetFocused = _targetPids.Contains(fgPid) || IsProcessNameMatch(fgPid);

        // 대상 앱이 포커스면 작업 중 (타블렛/펜 입력은 GetLastInputInfo로 감지 불가하므로 포커스만 체크)
        if (targetFocused)
        {
            _idleSeconds = 0;
            // 작업 시간 기록
            _sessionSeconds++;
            _workLog.AddSecond();
            _saveCounter++;
            // 30초마다 저장
            if (_saveCounter >= 30)
            {
                _workLog.Save();
                _saveCounter = 0;
            }
        }
        else
        {
            _idleSeconds += 1;
        }

        var threshold = _settings.IdleThresholdSeconds;

        if (_idleSeconds < threshold * 0.3)
        {
            UpdateState(PetState.Happy, 0);
        }
        else if (_idleSeconds < threshold)
        {
            UpdateState(PetState.Idle, 0);
        }
        else
        {
            var annoyance = Math.Min(1.0, (_idleSeconds - threshold) / (threshold * 2.0));
            UpdateState(PetState.Annoyed, annoyance);
        }
    }

    private void RefreshTargetProcesses()
    {
        var procs = Process.GetProcessesByName(_settings.TargetProcessName);
        _targetRunning = procs.Length > 0;
        _targetPids = new HashSet<uint>(procs.Select(p => (uint)p.Id));
        foreach (var p in procs) p.Dispose();
    }

    private bool IsProcessNameMatch(uint pid)
    {
        try
        {
            if (pid == 0) return false;
            var proc = Process.GetProcessById((int)pid);
            var match = proc.ProcessName.Contains(_settings.TargetProcessName, StringComparison.OrdinalIgnoreCase);
            proc.Dispose();
            return match;
        }
        catch { return false; }
    }

    private void UpdateState(PetState state, double annoyance)
    {
        CurrentState = state;
        AnnoyanceLevel = annoyance;
        StateUpdated?.Invoke(state, annoyance);
    }

    public void Dispose()
    {
        _timer.Stop();
        _workLog.Save();
    }
}
