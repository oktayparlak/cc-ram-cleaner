namespace CCRamCleaner.Core.Processes;

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

    /// <summary>
    /// The verified parent chain of a process (nearest first). A parent only counts if it was created
    /// no later than its child, so a pid reused by a younger process is never mistaken for an ancestor.
    /// </summary>
    public static IEnumerable<ProcessInfo> Ancestors(IReadOnlyDictionary<int, ProcessInfo> byPid, ProcessInfo process)
    {
        var seen = new HashSet<int> { process.Pid };
        var child = process;
        while (child.ParentPid != 0 && seen.Add(child.ParentPid) && byPid.TryGetValue(child.ParentPid, out var parent))
        {
            if (parent.CreationTime > child.CreationTime) yield break;
            yield return parent;
            child = parent;
        }
    }

    public static Dictionary<int, ProcessInfo> ByPid(IEnumerable<ProcessInfo> snapshot)
    {
        var map = new Dictionary<int, ProcessInfo>();
        foreach (var p in snapshot) map.TryAdd(p.Pid, p);
        return map;
    }
}
