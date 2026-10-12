using RamCleaner.Core.Classification;
using RamCleaner.Core.Config;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class ProcessClassifierTests
{
    private static ProcessInfo P(string name, int pid = 100, int session = 1, bool critical = false, int parent = 0) =>
        new(pid, name, session, 0, parent, critical, TimeSpan.Zero);

    private static ProcessClassifier C(EffectiveRules rules, params int[] shielded) => new(rules, shielded.ToHashSet());

    [Fact]
    public void Protected_BeatsUserBlock()
    {
        var c = C(TestRules.Make(userBlocked: ["explorer"]));
        Assert.Equal(Verdict.Protected, c.Classify(P("explorer")));
        Assert.False(c.ShouldKill(P("explorer")));
    }

    [Fact]
    public void UserAllowed_BeatsDefaultBlock()
    {
        var c = C(TestRules.Make(userAllowed: ["spotify"]));
        Assert.Equal(Verdict.UserAllowed, c.Classify(P("Spotify")));
        Assert.False(c.ShouldKill(P("Spotify")));
    }

    [Fact]
    public void UserBlocked_BeatsDevAllowed()
    {
        var c = C(TestRules.Make(userBlocked: ["chrome"]));
        Assert.Equal(Verdict.UserBlocked, c.Classify(P("chrome")));
        Assert.True(c.ShouldKill(P("chrome")));
    }

    [Fact]
    public void DefaultBlocked_IsKilled_DevToolIsNot()
    {
        var c = C(TestRules.Make());
        Assert.True(c.ShouldKill(P("Spotify")));
        Assert.Equal(Verdict.DevAllowed, c.Classify(P("com.docker.backend")));
        Assert.False(c.ShouldKill(P("claude")));
    }

    [Fact]
    public void Balanced_LeavesUnknownAlone()
    {
        var c = C(TestRules.Make(GuardProfile.Balanced));
        Assert.Equal(Verdict.Unknown, c.Classify(P("notepad")));
        Assert.False(c.ShouldKill(P("notepad")));
    }

    [Fact]
    public void Aggressive_KillsUnknownOnlyInUserSession()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive));
        Assert.True(c.ShouldKill(P("notepad", session: 1)));
        Assert.False(c.ShouldKill(P("SomeVendorService", session: 0)));
    }

    [Fact]
    public void CriticalSystemAndShieldedPids_AreProtected()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive), 777);
        Assert.Equal(Verdict.Protected, c.Classify(P("whatever", critical: true)));
        Assert.Equal(Verdict.Protected, c.Classify(P("System", pid: 4)));
        Assert.Equal(Verdict.Protected, c.Classify(P("MyLauncher", pid: 777)));
    }

    [Fact]
    public void Antivirus_IsProtected_EvenWhenUserBlocksIt()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive, userBlocked: ["avp"]));
        Assert.False(c.ShouldKill(P("avp")));
        Assert.False(c.ShouldKill(P("MsMpEng", session: 0)));
    }

    [Fact]
    public void NoDefaultBlockedProcessIsProtected()
    {
        foreach (var rule in DefaultRules.Load().BlockedProcesses)
            Assert.False(ProtectedProcesses.IsProtected(rule.Name), rule.Name);
    }

    [Fact]
    public void SelfAndAncestors_FollowsParentChain()
    {
        var snapshot = new[] { P("app", pid: 10, parent: 20), P("terminal", pid: 20, parent: 30), P("explorer", pid: 30, parent: 0), P("other", pid: 40, parent: 30) };
        Assert.Equal(new[] { 10, 20, 30 }, ProcessTree.SelfAndAncestors(snapshot, 10).Order());
    }

    [Fact]
    public void SelfAndAncestors_StopsOnPidReuseCycle()
    {
        var snapshot = new[] { P("a", pid: 5, parent: 6), P("b", pid: 6, parent: 5) };
        Assert.Equal(new[] { 5, 6 }, ProcessTree.SelfAndAncestors(snapshot, 5).Order());
    }
}
