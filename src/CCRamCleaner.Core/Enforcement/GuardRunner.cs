using CCRamCleaner.Core.Config;
using CCRamCleaner.Core.Memory;
using CCRamCleaner.Core.Processes;

namespace CCRamCleaner.Core.Enforcement;

/// <summary>Drives the Guard while active: WMI start events, 2 s process poll, 5 s service check, 10 s memory tick.</summary>
public sealed class GuardRunner : IDisposable
{
    private static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ServiceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MemoryInterval = TimeSpan.FromSeconds(10);

    private readonly Guard _guard;
    private readonly ActivityLog _log;
    private readonly Func<UserConfig> _config;
    private readonly WorkingSetTrimmer _trimmer;
    private readonly Timer _processTimer;
    private readonly Timer _serviceTimer;
    private readonly Timer _memoryTimer;
    private readonly object _gate = new();
    private ProcessStartWatcher? _watcher;
    private DateTime _lastTrim = DateTime.MinValue;
    private DateTime? _lastPurge;
    private int _memoryBusy;
    private bool _disposed;
    private bool _started;

    public GuardRunner(Guard guard, IProcessControl processes, ActivityLog log, Func<UserConfig> config)
    {
        _guard = guard;
        _log = log;
        _config = config;
        _trimmer = new WorkingSetTrimmer(processes, SystemMemory.ForegroundProcessId);
        _processTimer = new Timer(_ => Safe(_guard.EnforceProcesses));
        _serviceTimer = new Timer(_ => Safe(_guard.EnforceServices));
        _memoryTimer = new Timer(_ => Safe(MemoryTick));
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
            _processTimer.Change(ProcessInterval, ProcessInterval);
            _serviceTimer.Change(ServiceInterval, ServiceInterval);
            _memoryTimer.Change(MemoryInterval, MemoryInterval);
            try
            {
                _watcher = new ProcessStartWatcher(pid => Safe(() => _guard.OnProcessStarted(pid)));
                _watcher.Start();
            }
            catch (Exception ex)
            {
                _watcher?.Dispose();
                _watcher = null;
                _log.Write(LogKind.Warning, $"Process start events unavailable, using 2 s polling only: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _started = false;
            _processTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _serviceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _memoryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_watcher != null)
            {
                Safe(_watcher.Stop);
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    public void PurgeStandby()
    {
        var ok = SystemMemory.PurgeStandbyList();
        _log.Write(ok ? LogKind.Info : LogKind.Warning,
            ok ? "Standby cache purged" : "Standby cache purge failed (administrator rights required)");
    }

    private void MemoryTick()
    {
        if (Interlocked.CompareExchange(ref _memoryBusy, 1, 0) != 0) return;
        try
        {
            var config = _config();
            var now = DateTime.Now;
            if (config.TrimEnabled && now - _lastTrim >= TimeSpan.FromSeconds(config.TrimIntervalSeconds))
            {
                _lastTrim = now;
                var result = _trimmer.TrimIdle();
                _guard.AddReclaimed(result.WorkingSetReducedBytes);
                if (result.ProcessCount > 0)
                    _log.Write(LogKind.Info, $"Trimmed {result.ProcessCount} idle process(es), working set -{result.WorkingSetReducedBytes / (1024 * 1024)} MB");
            }
            if (config.AutoStandbyPurge && StandbyPolicy.ShouldPurge(SystemMemory.Read(), config.StandbyPurgeThresholdPercent, _lastPurge, now))
            {
                _lastPurge = now;
                PurgeStandby();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _memoryBusy, 0);
        }
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _processTimer.Dispose();
            _serviceTimer.Dispose();
            _memoryTimer.Dispose();
        }
    }
}
