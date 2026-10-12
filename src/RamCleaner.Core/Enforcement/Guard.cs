using RamCleaner.Core.Classification;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;

namespace RamCleaner.Core.Enforcement;

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
    private ProcessClassifier? _classifier;
    private long _reclaimed;

    public event Action<BlockedProcessEvent>? ProcessBlocked;

    public bool IsActive
    {
        get { lock (_gate) return _classifier != null; }
    }

    public long ReclaimedBytes => Interlocked.Read(ref _reclaimed);

    public void AddReclaimed(long bytes) => Interlocked.Add(ref _reclaimed, Math.Max(0, bytes));

    public PreviewResult Preview(EffectiveRules rules)
    {
        var snapshot = processes.Snapshot();
        var classifier = CreateClassifier(rules, snapshot);
        var targets = snapshot.Where(classifier.ShouldKill).OrderByDescending(p => p.WorkingSetBytes).ToList();
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
            _classifier = CreateClassifier(rules, processes.Snapshot());
            log.Write(LogKind.Info, $"Guard activated ({rules.Profile})");
            SweepProcesses(notify: false);
        }
    }

    public void UpdateRules(EffectiveRules rules)
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            _classifier = CreateClassifier(rules, processes.Snapshot());
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

    public void Deactivate()
    {
        lock (_gate)
        {
            if (_classifier == null && !journal.Exists) return;
            _classifier = null;
            RestoreServices(_ => true);
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
            if (_classifier == null) return;
            if (processes.TryGet(pid) is { } p) TryKill(p, notify: true);
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

    private void RestoreServices(Func<ServiceJournalEntry, bool> shouldRestore)
    {
        var keep = new List<ServiceJournalEntry>();
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
                if (entry.WasRunning && entry.OriginalStartType != ServiceStartType.Disabled
                    && services.Query(entry.Name) is { IsRunning: false })
                {
                    services.Start(entry.Name);
                }
                log.Write(LogKind.Restored, $"Service {entry.Name} restored ({entry.OriginalStartType})");
            }
            catch (Exception ex)
            {
                keep.Add(entry);
                log.Write(LogKind.Warning, $"Could not restore service {entry.Name}: {ex.Message}");
            }
        }
        journal.Replace(keep);
    }

    private void SweepProcesses(bool notify)
    {
        foreach (var p in processes.Snapshot()) TryKill(p, notify);
    }

    private void TryKill(ProcessInfo p, bool notify)
    {
        var classifier = _classifier!;
        if (!classifier.ShouldKill(p) || _unkillable.Contains((p.Pid, p.Name))) return;

        if (processes.Kill(p.Pid))
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
