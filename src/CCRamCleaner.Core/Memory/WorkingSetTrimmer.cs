using CCRamCleaner.Core.Classification;
using CCRamCleaner.Core.Processes;
using CCRamCleaner.Core.Rules;

namespace CCRamCleaner.Core.Memory;

public sealed record TrimResult(int ProcessCount, long WorkingSetReducedBytes);

/// <summary>
/// Empties the working set of processes that used (almost) no CPU since the previous call.
/// Not thread-safe: call from a single timer.
/// </summary>
public sealed class WorkingSetTrimmer(IProcessControl processes, Func<int> foregroundPid)
{
    private static readonly TimeSpan IdleCpuThreshold = TimeSpan.FromMilliseconds(50);
    private static readonly string[] Excluded = ["vmmem*"];

    private Dictionary<int, (string Name, TimeSpan Cpu)> _previous = [];

    public TrimResult TrimIdle()
    {
        var foreground = foregroundPid();
        var current = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        var trimmed = new Dictionary<int, long>();

        foreach (var p in processes.Snapshot())
        {
            current[p.Pid] = (p.Name, p.CpuTime);
            if (p.Pid == foreground || p.Pid == processes.CurrentProcessId || p.IsCritical
                || ProtectedProcesses.IsProtected(p.Name) || NamePattern.MatchesAny(Excluded, p.Name))
                continue;
            if (!_previous.TryGetValue(p.Pid, out var prev) || !string.Equals(prev.Name, p.Name, StringComparison.Ordinal))
                continue;
            if (p.CpuTime - prev.Cpu > IdleCpuThreshold) continue;
            if (processes.TrimWorkingSet(p.Pid)) trimmed[p.Pid] = p.WorkingSetBytes;
        }

        _previous = current;
        if (trimmed.Count == 0) return new TrimResult(0, 0);

        long reduced = 0;
        foreach (var p in processes.Snapshot())
            if (trimmed.TryGetValue(p.Pid, out var before)) reduced += Math.Max(0, before - p.WorkingSetBytes);
        return new TrimResult(trimmed.Count, reduced);
    }
}
