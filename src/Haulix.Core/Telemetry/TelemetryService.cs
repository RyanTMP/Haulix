using System.Diagnostics;

namespace Haulix.Core.Telemetry;

public enum GameState { NotDetected, NotRunning, Running }

public enum TelemetryState { Unavailable, Waiting, Live, Paused, Stale, Unsupported, Demo }

public enum GameEventType { JobStarted, JobDelivered, JobCancelled, Fined, Tollgate, Ferry, Train, Refuel }

public sealed record GameEvent(GameEventType Type, TelemetrySnapshot Snapshot, DateTime AtUtc);

public sealed record TelemetryStatus(GameState Game, TelemetryState Telemetry, uint PluginRevision, string? GameVersion, DateTime? LastSampleUtc, bool Demo);

/// <summary>
/// Polls the telemetry source on a background thread, tracks game/telemetry state and raises
/// edge-triggered gameplay events. All events fire on the polling thread.
/// </summary>
public sealed class TelemetryService : IDisposable
{
    private readonly ScsTelemetrySource _scs = new();
    private DemoTelemetrySource? _demo;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    private GameplayFlags _prevFlags = new();
    private bool _prevOnJob;
    private string _prevJobKey = "";
    private ulong _lastTimestamp;
    private DateTime _lastTimestampChangeUtc;
    private DateTime _lastProcessCheckUtc = DateTime.MinValue;
    private bool _gameRunning;
    private bool _primed;

    public TelemetryService(Func<bool> isGameInstalled)
    {
        IsGameInstalled = isGameInstalled;
    }

    public Func<bool> IsGameInstalled { get; set; }

    public int IntervalMs { get; set; } = 100;

    public TelemetrySnapshot? Last { get; private set; }

    public TelemetryStatus Status { get; private set; } = new(GameState.NotRunning, TelemetryState.Unavailable, 0, null, null, false);

    public bool DemoActive => _demo is not null;

    public event Action<TelemetrySnapshot>? Sample;
    public event Action<TelemetryStatus>? StatusChanged;
    public event Action<GameEvent>? GameEvent;

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "Haulix telemetry" };
        _thread.Start();
    }

    public void SetDemo(bool on)
    {
        lock (_gate)
        {
            if (on && _demo is null) _demo = new DemoTelemetrySource();
            else if (!on && _demo is not null) { _demo.Dispose(); _demo = null; Last = null; }
            ResetEdges();
        }
    }

    private void ResetEdges()
    {
        _prevFlags = new GameplayFlags();
        _prevOnJob = false;
        _prevJobKey = "";
        _primed = _demo is not null;
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Telemetry tick failed: {ex}");
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var wait = Math.Max(10, IntervalMs - (int)elapsed);
            ct.WaitHandle.WaitOne(wait);
        }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProcessCheckUtc).TotalSeconds >= 2)
        {
            _lastProcessCheckUtc = now;
            _gameRunning = IsProcessRunning();
            if (!_gameRunning) _scs.Release(); // let the game free its shared memory
        }

        TelemetrySnapshot? s;
        TelemetryState tstate;
        lock (_gate)
        {
            if (_demo is not null)
            {
                s = _demo.Read();
                tstate = TelemetryState.Demo;
            }
            else if (!_gameRunning)
            {
                s = null;
                tstate = TelemetryState.Unavailable;
            }
            else
            {
                s = _scs.Read();
                if (s is null) tstate = TelemetryState.Waiting;
                else if (s.PluginRevision != ScsTelemetryParser.SupportedRevision) { tstate = TelemetryState.Unsupported; s = null; }
                else if (!s.SdkActive) { tstate = TelemetryState.Waiting; s = null; }
                else
                {
                    if (s.SdkTimestamp != _lastTimestamp)
                    {
                        _lastTimestamp = s.SdkTimestamp;
                        _lastTimestampChangeUtc = now;
                    }
                    tstate = s.Paused ? TelemetryState.Paused
                        : (now - _lastTimestampChangeUtc).TotalSeconds > 3 ? TelemetryState.Stale
                        : TelemetryState.Live;
                }
            }
        }

        var game = _demo is not null || _gameRunning ? GameState.Running
            : IsGameInstalled() ? GameState.NotRunning : GameState.NotDetected;
        var status = new TelemetryStatus(game, tstate, s?.PluginRevision ?? Status.PluginRevision, s?.GameVersion ?? Status.GameVersion,
            s is not null ? now : Status.LastSampleUtc, _demo is not null);
        if (status.Game != Status.Game || status.Telemetry != Status.Telemetry || status.Demo != Status.Demo)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
        else
        {
            Status = status;
        }

        if (s is null)
        {
            _primed = false;
            return;
        }
        Last = s;
        DetectEvents(s, now);
        Sample?.Invoke(s);
    }

    private void DetectEvents(TelemetrySnapshot s, DateTime now)
    {
        var f = s.Flags;
        if (!_primed)
        {
            // First sample after (re)connecting: plugin flags may still be latched from an
            // earlier event, so take them as the baseline instead of firing.
            _prevFlags = f;
            _prevOnJob = s.OnJob;
            _prevJobKey = s.OnJob ? $"{s.SourceCityId}|{s.DestinationCityId}|{s.CargoId}|{s.JobIncome}" : "";
            _primed = true;
            return;
        }
        void Edge(bool cur, bool prev, GameEventType type)
        {
            if (cur && !prev) GameEvent?.Invoke(new GameEvent(type, s, now));
        }

        // A new job: onJob rises, or the job identity changes while on a job.
        var jobKey = s.OnJob ? $"{s.SourceCityId}|{s.DestinationCityId}|{s.CargoId}|{s.JobIncome}" : "";
        if (s.OnJob && (!_prevOnJob || jobKey != _prevJobKey)) GameEvent?.Invoke(new GameEvent(GameEventType.JobStarted, s, now));
        _prevOnJob = s.OnJob;
        _prevJobKey = jobKey;

        Edge(f.JobDelivered, _prevFlags.JobDelivered, GameEventType.JobDelivered);
        Edge(f.JobCancelled, _prevFlags.JobCancelled, GameEventType.JobCancelled);
        Edge(f.Fined, _prevFlags.Fined, GameEventType.Fined);
        Edge(f.Tollgate, _prevFlags.Tollgate, GameEventType.Tollgate);
        Edge(f.Ferry, _prevFlags.Ferry, GameEventType.Ferry);
        Edge(f.Train, _prevFlags.Train, GameEventType.Train);
        Edge(f.RefuelPaid, _prevFlags.RefuelPaid, GameEventType.Refuel);
        _prevFlags = f;
    }

    private static bool IsProcessRunning()
    {
        foreach (var name in new[] { "eurotrucks2", "amtrucks" })
        {
            var procs = Process.GetProcessesByName(name);
            var any = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(1000);
        _scs.Dispose();
        _demo?.Dispose();
    }
}
