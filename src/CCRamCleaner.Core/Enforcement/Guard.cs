using CCRamCleaner.Core.Config;
using CCRamCleaner.Core.Classification;
using CCRamCleaner.Core.Processes;
using CCRamCleaner.Core.Rules;
using CCRamCleaner.Core.Services;
using CCRamCleaner.Core.State;

namespace CCRamCleaner.Core.Enforcement;

public sealed record PreviewResult(IReadOnlyList<ProcessInfo> Processes, IReadOnlyList<ServiceState> Services)
{
    public long EstimatedBytes => Processes.Sum(p => p.WorkingSetBytes);
}

public sealed record BlockedProcessEvent(string Name, Verdict Verdict);

public sealed class JournalWriteException(Exception inner)
    : Exception("Session journal could not be written: " + inner.Message, inner);

/// <summary>
/// The active/inactive state machine. All public members are serialized by one lock;
/// ProcessBlocked is raised under that lock, so handlers must not block (marshal with InvokeAsync).
/// </summary>
public sealed class Guard(IServiceControl services, IProcessControl processes, SessionJournal journal, ActivityLog log)
{
    private readonly object _gate = new();
    private readonly HashSet<(int Pid, string Name)> _unkillable = [];
    private readonly HashSet<string> _warnedServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(int Pid, long CreationTime)> _spared = [];
    private ProcessClassifier? _classifier;
    private volatile bool _active;
    private long _reclaimed;

    public event Action<BlockedProcessEvent>? ProcessBlocked;

    /// <summary>Lock-free so the UI never waits for a long activation or restore.</summary>
    public bool IsActive => _active;

    public long ReclaimedBytes => Interlocked.Read(ref _reclaimed);

    public void AddReclaimed(long bytes) => Interlocked.Add(ref _reclaimed, Math.Max(0, bytes));

    public PreviewResult Preview(EffectiveRules rules)
    {
        var snapshot = processes.Snapshot();
        var classifier = CreateClassifier(rules, snapshot);
        var byPid = ProcessTree.ByPid(snapshot);
        var targets = snapshot
            .Where(p => classifier.ShouldKill(p, byPid) && !KeepsOpenWork(p, classifier))
            .OrderByDescending(p => p.WorkingSetBytes)
            .ToList();
        var targetServices = rules.BlockedServices
            .Select(TryQuery)
            .OfType<ServiceState>()
            .Where(s => s.IsRunning || s.StartType != ServiceStartType.Disabled)
            .ToList();
        return new PreviewResult(targets, targetServices);
    }

    public void Activate(EffectiveRules rules)
    {
        lock (_gate)
        {
            if (_classifier != null) return;
            Interlocked.Exchange(ref _reclaimed, 0);
            _unkillable.Clear();
            _spared.Clear();
            _warnedServices.Clear();
            try
            {
                foreach (var name in rules.BlockedServices) DisableService(name);
            }
            catch (JournalWriteException)
            {
                RestoreServices(_ => true);
                throw;
            }
            var snapshot = processes.Snapshot();
            _classifier = CreateClassifier(rules, snapshot);
            _active = true;
            log.Write(LogKind.Info, $"Guard activated ({rules.Profile})");
            SpareOpenWindows(snapshot);
            SweepProcesses(notify: false);
        }
    }

    public void UpdateRules(EffectiveRules rules)
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            var snapshot = processes.Snapshot();
            _classifier = CreateClassifier(rules, snapshot);
            SpareOpenWindows(snapshot);
            var blocked = new HashSet<string>(rules.BlockedServices, StringComparer.OrdinalIgnoreCase);
            RestoreServices(e => !blocked.Contains(e.Name));
            try
            {
                foreach (var name in rules.BlockedServices) DisableService(name);
            }
            catch (JournalWriteException ex)
            {
                log.Write(LogKind.Warning, ex.Message);
            }
            SweepProcesses(notify: false);
        }
    }

    /// <param name="restartServices">
    /// False on logoff/shutdown: start types are put back (fast) but nothing is started; Automatic services start at next boot.
    /// </param>
    public void Deactivate(bool restartServices = true)
    {
        lock (_gate)
        {
            if (_classifier == null && !journal.Exists) return;
            _classifier = null;
            _active = false;
            RestoreServices(_ => true, restartServices);
            log.Write(LogKind.Info, "Guard deactivated");
        }
    }

    /// <summary>Call once at startup: undoes service changes left behind by a crashed session.</summary>
    public int RecoverIfNeeded()
    {
        lock (_gate)
        {
            if (_classifier != null || !journal.Exists) return 0;
            var count = journal.Load().Count;
            RestoreServices(_ => true);
            log.Write(LogKind.Restored, $"Recovered {count} service(s) from a previous session");
            return count;
        }
    }

    public void EnforceProcesses()
    {
        lock (_gate)
        {
            if (_classifier != null) SweepProcesses(notify: true);
        }
    }

    public void OnProcessStarted(int pid)
    {
        lock (_gate)
        {
            if (_classifier == null || processes.TryGet(pid) is not { } p) return;
            // Only an Unknown process in Aggressive needs its ancestry (dev-tool children are spared); skip the full snapshot otherwise.
            var byPid = _classifier.Rules.Profile == GuardProfile.Aggressive && _classifier.Classify(p) == Verdict.Unknown
                ? ProcessTree.ByPid(processes.Snapshot())
                : null;
            TryKill(p, byPid, notify: true);
        }
    }

    public void EnforceServices()
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            foreach (var entry in journal.Load())
            {
                try
                {
                    var state = services.Query(entry.Name);
                    if (state is null) continue;
                    if (state.StartType != ServiceStartType.Disabled)
                        services.SetStartType(entry.Name, ServiceStartType.Disabled, false);
                    if (state.IsRunning)
                    {
                        services.Stop(entry.Name);
                        log.Write(LogKind.ServiceStopped, $"Service {entry.Name} was restarted — stopped again");
                    }
                }
                catch (Exception ex)
                {
                    if (_warnedServices.Add(entry.Name))
                        log.Write(LogKind.Warning, $"Service {entry.Name}: {ex.Message}");
                }
            }
        }
    }

    private void DisableService(string name)
    {
        var state = TryQuery(name);
        if (state is null || (state.StartType == ServiceStartType.Disabled && !state.IsRunning)) return;

        try
        {
            journal.Record(new ServiceJournalEntry(state.Name, state.StartType, state.DelayedAutoStart, state.IsRunning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JournalWriteException(ex);
        }

        try
        {
            services.SetStartType(state.Name, ServiceStartType.Disabled, false);
            if (state.IsRunning)
            {
                var bytes = OwnProcessBytes(state);
                services.Stop(state.Name);
                AddReclaimed(bytes);
            }
            log.Write(LogKind.ServiceStopped, $"Service {state.Name} disabled{(state.IsRunning ? " and stopped" : "")}");
        }
        catch (Exception ex)
        {
            log.Write(LogKind.Warning, $"Service {state.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pass 1 writes every original start type back and shrinks the journal — fast, and enough to leave the
    /// system correct if we are killed mid-way (logoff, forced shutdown). Pass 2 restarts what was running.
    /// </summary>
    private void RestoreServices(Func<ServiceJournalEntry, bool> shouldRestore, bool restartServices = true)
    {
        var keep = new List<ServiceJournalEntry>();
        var restored = new List<ServiceJournalEntry>();
        foreach (var entry in journal.Load())
        {
            if (!shouldRestore(entry))
            {
                keep.Add(entry);
                continue;
            }
            try
            {
                if (services.Query(entry.Name) is null) continue; // uninstalled meanwhile
                services.SetStartType(entry.Name, entry.OriginalStartType, entry.OriginalDelayedAutoStart);
                restored.Add(entry);
                log.Write(LogKind.Restored, $"Service {entry.Name} restored ({entry.OriginalStartType})");
            }
            catch (Exception ex)
            {
                keep.Add(entry);
                log.Write(LogKind.Warning, $"Could not restore service {entry.Name}: {ex.Message}");
            }
        }
        journal.Replace(keep);

        if (!restartServices) return;
        foreach (var entry in restored.Where(e => e.WasRunning && e.OriginalStartType != ServiceStartType.Disabled))
        {
            try
            {
                if (services.Query(entry.Name) is { IsRunning: false }) services.Start(entry.Name);
            }
            catch (Exception ex)
            {
                log.Write(LogKind.Warning, $"Could not restart service {entry.Name}: {ex.Message}");
            }
        }
    }

    private void SweepProcesses(bool notify)
    {
        var snapshot = processes.Snapshot();
        var byPid = ProcessTree.ByPid(snapshot);
        foreach (var p in snapshot) TryKill(p, byPid, notify);
    }

    /// <summary>
    /// Unknown apps that already have an open window when the guard (re)evaluates may hold unsaved work:
    /// they are left running for this session instead of being force-killed. New launches are still blocked.
    /// </summary>
    private void SpareOpenWindows(IReadOnlyList<ProcessInfo> snapshot)
    {
        var classifier = _classifier!;
        var byPid = ProcessTree.ByPid(snapshot);
        foreach (var p in snapshot)
        {
            if (!KeepsOpenWork(p, classifier) || !classifier.ShouldKill(p, byPid) || !_spared.Add((p.Pid, p.CreationTime))) continue;
            log.Write(LogKind.Info, $"{p.Name} ({p.Pid}) left running — it has an open window");
        }
    }

    private static bool KeepsOpenWork(ProcessInfo p, ProcessClassifier classifier) =>
        p.HasWindow && classifier.Classify(p) == Verdict.Unknown;

    private void TryKill(ProcessInfo p, IReadOnlyDictionary<int, ProcessInfo>? byPid, bool notify)
    {
        var classifier = _classifier!;
        if (!classifier.ShouldKill(p, byPid) || _unkillable.Contains((p.Pid, p.Name)) || _spared.Contains((p.Pid, p.CreationTime))) return;

        if (processes.Kill(p))
        {
            AddReclaimed(p.WorkingSetBytes);
            log.Write(LogKind.Killed, $"{p.Name} ({p.Pid}) stopped — {p.WorkingSetBytes / (1024 * 1024)} MB");
            if (notify) ProcessBlocked?.Invoke(new BlockedProcessEvent(p.Name, classifier.Classify(p)));
        }
        else
        {
            _unkillable.Add((p.Pid, p.Name));
            log.Write(LogKind.Warning, $"Could not stop {p.Name} ({p.Pid})");
        }
    }

    private long OwnProcessBytes(ServiceState state)
    {
        if (state.ProcessId == 0) return 0;
        var p = processes.TryGet(state.ProcessId);
        return p is null || NamePattern.Matches("svchost", p.Name) ? 0 : p.WorkingSetBytes;
    }

    private ServiceState? TryQuery(string name)
    {
        try
        {
            return services.Query(name);
        }
        catch (Exception ex)
        {
            log.Write(LogKind.Warning, $"Cannot query service {name}: {ex.Message}");
            return null;
        }
    }

    private ProcessClassifier CreateClassifier(EffectiveRules rules, IReadOnlyList<ProcessInfo> snapshot) =>
        new(rules, ProcessTree.SelfAndAncestors(snapshot, processes.CurrentProcessId));
}
