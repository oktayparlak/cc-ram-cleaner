using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

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
        _proc.Add(40, "RamCleanerDev", parent: 41);
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
}
