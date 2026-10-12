using CCRamCleaner.Core.Config;
using CCRamCleaner.Core.Enforcement;
using CCRamCleaner.Core.Services;
using CCRamCleaner.Core.State;
using CCRamCleaner.Core.Tests.TestSupport;

namespace CCRamCleaner.Core.Tests;

public class GuardProcessTests
{
    private readonly FakeServiceControl _svc = new();
    private readonly FakeProcessControl _proc = new();
    private readonly ActivityLog _log = new();
    private readonly Guard _guard;
    private readonly List<BlockedProcessEvent> _events = [];

    public GuardProcessTests()
    {
        _guard = new Guard(_svc, _proc, new SessionJournal(TempPaths.NewFile("session.json")), _log);
        _guard.ProcessBlocked += _events.Add;
    }

    [Fact]
    public void Activate_KillsBlockedProcesses_AndKeepsDevTools()
    {
        _proc.Add(10, "Spotify", mb: 600);
        _proc.Add(11, "claude");
        _proc.Add(12, "chrome");
        _proc.Add(13, "com.docker.backend");

        _guard.Activate(TestRules.Make());

        Assert.Equal(new[] { 10 }, _proc.Killed);
        Assert.Equal(600 * FakeProcessControl.MB, _guard.ReclaimedBytes);
    }

    [Fact]
    public void InitialSweep_DoesNotNotify_ButRelaunchDoes()
    {
        _proc.Add(10, "Spotify");
        _guard.Activate(TestRules.Make());
        Assert.Empty(_events);

        _proc.Add(20, "Spotify");
        _guard.OnProcessStarted(20);

        Assert.Contains(20, _proc.Killed);
        Assert.Equal("Spotify", Assert.Single(_events).Name);
    }

    [Fact]
    public void Aggressive_KillsUnknownUserSessionProcesses_Only()
    {
        _proc.Add(30, "notepad", session: 1);
        _proc.Add(31, "VendorAgent", session: 0);
        _proc.Add(32, "explorer", session: 1);
        _proc.Add(33, "claude", session: 1);
        _proc.Add(34, "weird", session: 1, critical: true);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        Assert.Equal(new[] { 30 }, _proc.Killed);
    }

    [Fact]
    public void Aggressive_NeverKillsOwnProcessOrItsAncestors()
    {
        _proc.CurrentProcessId = 40;
        _proc.Add(40, "CCRamCleanerDev", parent: 41);
        _proc.Add(41, "SomeLauncher", parent: 0);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void UpdateRules_AllowingAProcess_StopsKillingIt()
    {
        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));
        _guard.UpdateRules(TestRules.Make(GuardProfile.Aggressive, userAllowed: ["notepad"]));

        _proc.Add(50, "notepad");
        _guard.OnProcessStarted(50);

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void UnkillableProcess_IsNotRetried_ButReusedPidWithOtherNameIs()
    {
        _proc.Add(60, "Spotify");
        _proc.FailKill.Add(60);
        var rules = TestRules.Make(blocked: ["Spotify", "Discord"]);
        _guard.Activate(rules);
        _guard.EnforceProcesses();
        _guard.EnforceProcesses();

        Assert.Single(_log.Snapshot(), e => e.Kind == LogKind.Warning && e.Message.Contains("(60)"));

        _proc.Processes.Clear();
        _proc.FailKill.Clear();
        _proc.Add(60, "Discord");
        _guard.EnforceProcesses();

        Assert.Contains(60, _proc.Killed);
    }

    [Fact]
    public void InactiveGuard_DoesNotKill()
    {
        _guard.Activate(TestRules.Make());
        _guard.Deactivate();

        _proc.Add(70, "Spotify");
        _guard.OnProcessStarted(70);
        _guard.EnforceProcesses();

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void Preview_ListsTargets_WithoutChangingAnything()
    {
        _proc.Add(80, "Spotify", mb: 600);
        _proc.Add(81, "claude");
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("Fax", ServiceStartType.Disabled, running: false);

        var preview = _guard.Preview(TestRules.Make(services: ["DiagTrack", "Fax"]));

        Assert.Equal(80, Assert.Single(preview.Processes).Pid);
        Assert.Equal("DiagTrack", Assert.Single(preview.Services).Name);
        Assert.Equal(600 * FakeProcessControl.MB, preview.EstimatedBytes);
        Assert.Empty(_proc.Killed);
        Assert.Empty(_svc.Calls);
        Assert.False(_guard.IsActive);
    }

    // C1: helpers spawned by dev tools (git-credential-manager, esbuild, testhost, rg…) must survive Aggressive.
    [Fact]
    public void Aggressive_SparesUnknownDescendantsOfDevTools_ButNotOtherUnknowns()
    {
        _proc.Add(200, "explorer", parent: 0);
        _proc.Add(100, "node", parent: 200);
        _proc.Add(101, "esbuild", parent: 100);
        _proc.Add(102, "git", parent: 200);
        _proc.Add(103, "git-credential-manager", parent: 102);
        _proc.Add(104, "helper-of-helper", parent: 103);
        _proc.Add(105, "notepad", parent: 200);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive, dev: ["node", "git"]));

        Assert.Equal(new[] { 105 }, _proc.Killed);
    }

    [Fact]
    public void Aggressive_SparesDevToolChildStartedAfterActivation()
    {
        _proc.Add(100, "node");
        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        _proc.Add(110, "testhost", parent: 100);
        _guard.OnProcessStarted(110);

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void Aggressive_ReusedParentPid_DoesNotShieldOlderProcess()
    {
        // pid 300 now belongs to a node started *after* the orphan, so it cannot be the orphan's real parent
        _proc.Add(300, "node", created: 50);
        _proc.Add(301, "orphan", parent: 300, created: 10);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        Assert.Equal(new[] { 301 }, _proc.Killed);
    }

    // I7: an unknown app with an open window at activation may hold unsaved work.
    [Fact]
    public void Aggressive_LeavesAppsWithOpenWindowsRunning_AtActivation()
    {
        _proc.Add(400, "WINWORD", window: true);
        _proc.Add(401, "bgjunk");

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));
        _guard.EnforceProcesses();

        Assert.Equal(new[] { 401 }, _proc.Killed);
        Assert.Contains(_log.Snapshot(), e => e.Message.Contains("WINWORD"));
    }

    [Fact]
    public void Aggressive_StillBlocksWindowedAppLaunchedAfterActivation()
    {
        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        _proc.Add(402, "notepad", window: true);
        _guard.OnProcessStarted(402);

        Assert.Equal(new[] { 402 }, _proc.Killed);
    }

    [Fact]
    public void ExplicitlyBlockedApp_IsKilledEvenWithOpenWindow()
    {
        _proc.Add(403, "Spotify", window: true);
        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));
        Assert.Equal(new[] { 403 }, _proc.Killed);
    }

    [Fact]
    public void Preview_InAggressive_ExcludesSparedProcesses()
    {
        _proc.Add(100, "node");
        _proc.Add(101, "esbuild", parent: 100);
        _proc.Add(404, "WINWORD", window: true);
        _proc.Add(405, "bgjunk");

        var preview = _guard.Preview(TestRules.Make(GuardProfile.Aggressive));

        Assert.Equal(new[] { 405 }, preview.Processes.Select(p => p.Pid));
    }
}
