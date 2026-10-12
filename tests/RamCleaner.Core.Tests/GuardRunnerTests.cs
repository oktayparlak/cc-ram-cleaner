using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

// I5: a second Start (e.g. a racing tray toggle) must not create a second WMI subscription.
// Discriminates when run without administrator rights, where each subscription attempt logs a warning.
[Trait("Category", "Windows")]
public class GuardRunnerTests
{
    [Fact]
    public void Start_IsIdempotent()
    {
        var log = new ActivityLog();
        var processes = new FakeProcessControl();
        var guard = new Guard(new FakeServiceControl(), processes, new SessionJournal(TempPaths.NewFile("session.json")), log);
        using var runner = new GuardRunner(guard, processes, log, () => new UserConfig());

        runner.Start();
        runner.Start();

        Assert.True(log.Snapshot().Count(e => e.Message.StartsWith("Process start events unavailable")) <= 1);
    }
}
