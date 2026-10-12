using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class GuardServiceTests
{
    private readonly FakeServiceControl _svc = new();
    private readonly FakeProcessControl _proc = new();
    private readonly SessionJournal _journal = new(TempPaths.NewFile("session.json"));
    private readonly ActivityLog _log = new();

    private Guard NewGuard(SessionJournal? journal = null) => new(_svc, _proc, journal ?? _journal, _log);

    [Fact]
    public void Activate_DisablesAndStopsRunningService_AndJournalsOriginal()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();

        guard.Activate(TestRules.Make(services: ["DiagTrack"]));

        Assert.True(guard.IsActive);
        Assert.Equal(ServiceStartType.Disabled, _svc.Query("DiagTrack")!.StartType);
        Assert.False(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(new[] { new ServiceJournalEntry("DiagTrack", ServiceStartType.Automatic, false, true) }, _journal.Load());
    }

    [Fact]
    public void Deactivate_RestoresStartType_AndRestartsOnlyPreviouslyRunning()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("WSearch", ServiceStartType.Manual, running: false);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack", "WSearch"]));

        guard.Deactivate();

        Assert.False(guard.IsActive);
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("DiagTrack")!.StartType);
        Assert.True(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(ServiceStartType.Manual, _svc.Query("WSearch")!.StartType);
        Assert.False(_svc.Query("WSearch")!.IsRunning);
        Assert.DoesNotContain("start WSearch", _svc.Calls);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void Deactivate_RestoresDelayedAutoStart()
    {
        _svc.Add("SysMain", ServiceStartType.Automatic, running: true, delayed: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["SysMain"]));
        guard.Deactivate();
        Assert.True(_svc.Query("SysMain")!.DelayedAutoStart);
    }

    [Fact]
    public void Activate_SkipsMissingAndAlreadyDisabledServices()
    {
        _svc.Add("Fax", ServiceStartType.Disabled, running: false);
        NewGuard().Activate(TestRules.Make(services: ["Fax", "NotInstalled"]));
        Assert.Empty(_svc.Calls);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void RecoverIfNeeded_RestoresServicesFromCrashedSession()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        NewGuard().Activate(TestRules.Make(services: ["DiagTrack"]));
        // simulated crash: the first guard is never deactivated

        var restarted = NewGuard();

        Assert.Equal(1, restarted.RecoverIfNeeded());
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("DiagTrack")!.StartType);
        Assert.True(_svc.Query("DiagTrack")!.IsRunning);
        Assert.False(_journal.Exists);
        Assert.Equal(0, restarted.RecoverIfNeeded());
    }

    [Fact]
    public void Activate_WhenJournalCannotBeWritten_ThrowsAndChangesNothing()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var blocker = TempPaths.NewFile("blocker");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "");
        var guard = NewGuard(new SessionJournal(Path.Combine(blocker, "session.json")));

        Assert.Throws<JournalWriteException>(() => guard.Activate(TestRules.Make(services: ["DiagTrack"])));

        Assert.Empty(_svc.Calls);
        Assert.False(guard.IsActive);
    }

    [Fact]
    public void Activate_WhenStopFails_LogsWarning_AndDeactivateStillRestores()
    {
        _svc.Add("Spooler", ServiceStartType.Automatic, running: true);
        _svc.FailStop.Add("Spooler");
        var guard = NewGuard();

        guard.Activate(TestRules.Make(services: ["Spooler"]));

        Assert.Contains(_log.Snapshot(), e => e.Kind == LogKind.Warning && e.Message.Contains("Spooler"));
        guard.Deactivate();
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("Spooler")!.StartType);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void EnforceServices_StopsServiceThatWasRestartedWhileActive()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack"]));
        _svc.Services["DiagTrack"] = _svc.Services["DiagTrack"] with { IsRunning = true, StartType = ServiceStartType.Manual };

        guard.EnforceServices();

        Assert.False(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(ServiceStartType.Disabled, _svc.Query("DiagTrack")!.StartType);
        Assert.Equal(ServiceStartType.Automatic, Assert.Single(_journal.Load()).OriginalStartType);
    }

    [Fact]
    public void UpdateRules_KeepsOriginalJournalEntry()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        var rules = TestRules.Make(services: ["DiagTrack"]);
        guard.Activate(rules);

        guard.UpdateRules(rules);

        Assert.Equal(ServiceStartType.Automatic, Assert.Single(_journal.Load()).OriginalStartType);
    }

    [Fact]
    public void UpdateRules_RestoresServiceRemovedFromBlockList()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("WSearch", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack", "WSearch"]));

        guard.UpdateRules(TestRules.Make(services: ["DiagTrack"]));

        Assert.Equal(ServiceStartType.Automatic, _svc.Query("WSearch")!.StartType);
        Assert.True(_svc.Query("WSearch")!.IsRunning);
        Assert.Equal("DiagTrack", Assert.Single(_journal.Load()).Name);
    }

    [Fact]
    public void Deactivate_WhenServiceWasUninstalledMeanwhile_DropsJournalEntry()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack"]));
        _svc.Services.Remove("DiagTrack");

        guard.Deactivate();

        Assert.False(_journal.Exists);
    }
}
