using CCRamCleaner.Core.Memory;
using CCRamCleaner.Core.Processes;
using CCRamCleaner.Core.Tests.TestSupport;

namespace CCRamCleaner.Core.Tests;

public class MemoryTests
{
    private const ulong GB = 1024UL * 1024 * 1024;

    [Fact]
    public void MemoryStatus_ComputesPercentages()
    {
        var s = new MemoryStatus(16 * GB, 4 * GB);
        Assert.Equal(75, s.UsedPercent, 3);
        Assert.Equal(25, s.AvailablePercent, 3);
    }

    [Theory]
    [InlineData(1.0, null, true)]      // 6.25% available < 10, never purged
    [InlineData(1.0, 4.0, false)]      // purged 4 min ago
    [InlineData(1.0, 6.0, true)]       // purged 6 min ago
    [InlineData(4.0, null, false)]     // 25% available
    public void StandbyPolicy_ShouldPurge(double availableGb, double? minutesSinceLastPurge, bool expected)
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0);
        DateTime? last = minutesSinceLastPurge is { } m ? now.AddMinutes(-m) : null;
        var status = new MemoryStatus(16 * GB, (ulong)(availableGb * GB));
        Assert.Equal(expected, StandbyPolicy.ShouldPurge(status, 10, last, now));
    }

    private static ProcessInfo Proc(int pid, string name, int parent) => new(pid, name, 1, 0, parent, false, TimeSpan.Zero);

    [Fact]
    public void AgentSessionCounter_CountsTopLevelSessionsPerAgent_AndOmitsAgentsNotRunning()
    {
        var snapshot = new[]
        {
            Proc(1, "bash", 0),
            Proc(2, "claude", 1), Proc(3, "claude", 2),      // one session with a nested claude
            Proc(4, "claude.exe", 1), Proc(5, "node", 4),    // second session
            Proc(6, "opencode", 1), Proc(7, "opencode", 6),  // one OpenCode session
        };

        var counts = AgentSessionCounter.Count(snapshot);

        Assert.Equal(new[] { new AgentSessionCount("Claude Code", 2), new AgentSessionCount("OpenCode", 1) }, counts);
    }

    [Fact]
    public void AgentSessionCounter_CountsCodexNativeAndNpmVendorBinaries()
    {
        var snapshot = new[]
        {
            Proc(10, "node", 1), Proc(11, "codex-x86_64-pc-windows-msvc", 10), // npm launcher → vendor binary
            Proc(12, "codex", 1), Proc(13, "codex", 12),                       // native install with a nested codex
        };

        Assert.Equal(new[] { new AgentSessionCount("Codex", 2) }, AgentSessionCounter.Count(snapshot));
    }

    [Fact]
    public void AgentSessionCounter_NoAgentsRunning_ReturnsEmpty() =>
        Assert.Empty(AgentSessionCounter.Count(new[] { Proc(1, "bash", 0), Proc(2, "node", 1) }));

    [Fact]
    public void Trimmer_FirstPassTrimsNothing_SecondPassTrimsOnlyIdle()
    {
        var proc = new FakeProcessControl();
        proc.Add(10, "notepad", mb: 100, cpuSeconds: 1.0);
        proc.Add(11, "busyapp", mb: 100, cpuSeconds: 1.0);
        var trimmer = new WorkingSetTrimmer(proc, () => 0);

        Assert.Equal(0, trimmer.TrimIdle().ProcessCount);

        proc.SetCpu(10, 1.01);
        proc.SetCpu(11, 3.0);
        var result = trimmer.TrimIdle();

        Assert.Equal(new[] { 10 }, proc.Trimmed);
        Assert.Equal(1, result.ProcessCount);
        Assert.Equal(90 * FakeProcessControl.MB, result.WorkingSetReducedBytes);
    }

    [Fact]
    public void Trimmer_SkipsForegroundSelfVmmemProtectedAndCritical()
    {
        var proc = new FakeProcessControl { CurrentProcessId = 1 };
        proc.Add(1, "CCRamCleanerDev");
        proc.Add(2, "Code");          // foreground
        proc.Add(3, "vmmemWSL");
        proc.Add(4, "explorer");
        proc.Add(5, "weird", critical: true);
        var trimmer = new WorkingSetTrimmer(proc, () => 2);

        trimmer.TrimIdle();
        trimmer.TrimIdle();

        Assert.Empty(proc.Trimmed);
    }

    [Fact]
    public void Trimmer_DoesNotTrimWhenPidWasReusedByAnotherProcess()
    {
        var proc = new FakeProcessControl();
        proc.Add(10, "notepad");
        var trimmer = new WorkingSetTrimmer(proc, () => 0);
        trimmer.TrimIdle();

        proc.Processes.Clear();
        proc.Add(10, "mspaint");
        trimmer.TrimIdle();

        Assert.Empty(proc.Trimmed);
    }
}
