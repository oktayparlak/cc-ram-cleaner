using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Memory;

public static class ClaudeSessionCounter
{
    /// <summary>Counts claude processes whose parent is not itself a claude process (one per terminal session).</summary>
    public static int Count(IReadOnlyList<ProcessInfo> snapshot)
    {
        var claudePids = snapshot.Where(p => NamePattern.Matches("claude", p.Name)).Select(p => p.Pid).ToHashSet();
        return snapshot.Count(p => claudePids.Contains(p.Pid) && !claudePids.Contains(p.ParentPid));
    }
}
