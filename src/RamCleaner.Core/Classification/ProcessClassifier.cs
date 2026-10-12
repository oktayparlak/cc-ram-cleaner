using RamCleaner.Core.Config;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Classification;

public enum Verdict { Protected, UserAllowed, UserBlocked, DevAllowed, DefaultBlocked, Unknown }

public sealed class ProcessClassifier(EffectiveRules rules, IReadOnlySet<int> shieldedPids)
{
    public EffectiveRules Rules => rules;

    public Verdict Classify(ProcessInfo p)
    {
        if (p.Pid is 0 or 4 || p.IsCritical || shieldedPids.Contains(p.Pid) || ProtectedProcesses.IsProtected(p.Name))
            return Verdict.Protected;
        if (NamePattern.MatchesAny(rules.UserAllowedProcesses, p.Name)) return Verdict.UserAllowed;
        if (NamePattern.MatchesAny(rules.UserBlockedProcesses, p.Name)) return Verdict.UserBlocked;
        if (NamePattern.MatchesAny(rules.DevAllowedProcesses, p.Name)) return Verdict.DevAllowed;
        if (NamePattern.MatchesAny(rules.DefaultBlockedProcesses, p.Name)) return Verdict.DefaultBlocked;
        return Verdict.Unknown;
    }

    public bool ShouldKill(ProcessInfo p) => ShouldKill(p, null);

    /// <param name="byPid">Live process map; when given, Unknown processes descending from an allowed process are spared.</param>
    public bool ShouldKill(ProcessInfo p, IReadOnlyDictionary<int, ProcessInfo>? byPid) => Classify(p) switch
    {
        Verdict.UserBlocked or Verdict.DefaultBlocked => true,
        Verdict.Unknown => rules.Profile == GuardProfile.Aggressive && p.SessionId != 0 && !HasAllowedAncestor(p, byPid),
        _ => false,
    };

    /// <summary>Helpers spawned by dev tools (git-credential-manager, esbuild, testhost, rg…) inherit their allowance.</summary>
    public bool HasAllowedAncestor(ProcessInfo p, IReadOnlyDictionary<int, ProcessInfo>? byPid) =>
        byPid is not null && ProcessTree.Ancestors(byPid, p).Any(a => Classify(a) is Verdict.DevAllowed or Verdict.UserAllowed);
}
