namespace RamCleaner.Core.Processes;

public static class ProcessTree
{
    /// <summary>The pid plus its parent chain. Stops on cycles caused by pid reuse.</summary>
    public static HashSet<int> SelfAndAncestors(IReadOnlyList<ProcessInfo> snapshot, int pid)
    {
        var parents = new Dictionary<int, int>();
        foreach (var p in snapshot) parents.TryAdd(p.Pid, p.ParentPid);

        var result = new HashSet<int>();
        var current = pid;
        while (result.Add(current) && parents.TryGetValue(current, out var parent) && parent != 0)
            current = parent;
        return result;
    }
}
