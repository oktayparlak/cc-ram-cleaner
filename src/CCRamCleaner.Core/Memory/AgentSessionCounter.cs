using CCRamCleaner.Core.Processes;
using CCRamCleaner.Core.Rules;

namespace CCRamCleaner.Core.Memory;

public sealed record AgentSessionCount(string Agent, int Sessions);

public static class AgentSessionCounter
{
    /// <summary>Coding agents shown on the dashboard, in display order, with the process names they run as.</summary>
    public static readonly IReadOnlyList<(string Agent, string[] Patterns)> Agents =
    [
        ("Claude Code", ["claude"]),
        ("Codex", ["codex", "codex-*"]),       // native install, or the npm package's vendor binary
        ("OpenCode", ["opencode", "opencode-*"]),
    ];

    /// <summary>
    /// Sessions per agent that is running (agents with no session are omitted). A process counts as a session
    /// when its parent is not a process of the same agent, so one terminal session is counted once.
    /// </summary>
    public static IReadOnlyList<AgentSessionCount> Count(IReadOnlyList<ProcessInfo> snapshot)
    {
        var result = new List<AgentSessionCount>();
        foreach (var (agent, patterns) in Agents)
        {
            var pids = snapshot.Where(p => NamePattern.MatchesAny(patterns, p.Name)).Select(p => p.Pid).ToHashSet();
            var sessions = snapshot.Count(p => pids.Contains(p.Pid) && !pids.Contains(p.ParentPid));
            if (sessions > 0) result.Add(new AgentSessionCount(agent, sessions));
        }
        return result;
    }
}
