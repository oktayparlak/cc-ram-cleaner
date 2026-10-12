using System.Diagnostics;
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Services;

namespace RamCleaner.Core.Tests;

[Trait("Category", "Windows")]
public class WindowsIntegrationTests
{
    private readonly WindowsProcessControl _processes = new();
    private readonly WindowsServiceControl _services = new();

    [Fact]
    public void Snapshot_ContainsCurrentProcess_WithSessionAndParent()
    {
        var self = Assert.Single(_processes.Snapshot(), p => p.Pid == Environment.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().SessionId, self.SessionId);
        Assert.NotEqual(0, self.ParentPid);
        Assert.True(self.WorkingSetBytes > 0);
        Assert.False(self.IsCritical);
    }

    [Fact]
    public void TryGet_UnknownPid_ReturnsNull() => Assert.Null(_processes.TryGet(int.MaxValue - 1));

    [Fact]
    public void Kill_StopsAProcessWeStarted()
    {
        using var ping = StartPing();
        Assert.True(_processes.Kill(_processes.TryGet(ping.Id)!));
        Assert.True(ping.WaitForExit(5000));
    }

    // I1: a snapshot entry whose pid now belongs to a different (newer) process must not kill the newcomer.
    [Fact]
    public void Kill_LeavesProcessAlone_WhenItsIdentityDoesNotMatch()
    {
        using var ping = StartPing();
        var info = _processes.TryGet(ping.Id)!;
        Assert.NotEqual(0, info.CreationTime);

        Assert.True(_processes.Kill(info with { CreationTime = info.CreationTime - 1 }));
        Assert.False(ping.WaitForExit(500));

        Assert.True(_processes.Kill(info));
        Assert.True(ping.WaitForExit(5000));
    }

    private static Process StartPing() =>
        Process.Start(new ProcessStartInfo("ping", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;

    [Fact]
    public void TrimWorkingSet_OnOwnProcess_Succeeds() => Assert.True(_processes.TrimWorkingSet(Environment.ProcessId));

    [Fact]
    public void Query_KnownService_ReturnsState()
    {
        var state = _services.Query("EventLog");
        Assert.NotNull(state);
        Assert.True(state.IsRunning);
        Assert.Equal(ServiceStartType.Automatic, state.StartType);
        Assert.NotEqual(0, state.ProcessId);
    }

    [Fact]
    public void Query_MissingService_ReturnsNull() => Assert.Null(_services.Query("RamCleanerNoSuchService123"));

    [Fact]
    public void SystemMemory_ReadsSaneValues()
    {
        var m = SystemMemory.Read();
        Assert.True(m.TotalBytes > 1UL << 30);
        Assert.InRange(m.AvailableBytes, 1UL, m.TotalBytes);
    }
}
