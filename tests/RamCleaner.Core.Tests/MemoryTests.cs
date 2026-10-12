using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

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

    [Fact]
    public void ClaudeSessionCounter_CountsOnlyTopLevelClaudeProcesses()
    {
        ProcessInfo P(int pid, string name, int parent) => new(pid, name, 1, 0, parent, false, TimeSpan.Zero);
        var snapshot = new[] { P(1, "bash", 0), P(2, "claude", 1), P(3, "claude", 2), P(4, "claude.exe", 1), P(5, "node", 4) };
        Assert.Equal(2, ClaudeSessionCounter.Count(snapshot));
    }

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
        proc.Add(1, "RamCleanerDev");
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
