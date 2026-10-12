using CCRamCleaner.Core.Processes;

namespace CCRamCleaner.Core.Tests.TestSupport;

internal sealed class FakeProcessControl : IProcessControl
{
    public const long MB = 1024 * 1024;

    public List<ProcessInfo> Processes { get; } = [];
    public HashSet<int> FailKill { get; } = [];
    public List<int> Killed { get; } = [];
    public List<int> Trimmed { get; } = [];
    public int CurrentProcessId { get; set; } = 1;

    public ProcessInfo Add(int pid, string name, int session = 1, long mb = 100, int parent = 0, bool critical = false,
        double cpuSeconds = 0, long created = 0, bool window = false)
    {
        var p = new ProcessInfo(pid, name, session, mb * MB, parent, critical, TimeSpan.FromSeconds(cpuSeconds), created, window);
        Processes.Add(p);
        return p;
    }

    public void SetCpu(int pid, double seconds)
    {
        var i = Processes.FindIndex(p => p.Pid == pid);
        Processes[i] = Processes[i] with { CpuTime = TimeSpan.FromSeconds(seconds) };
    }

    public IReadOnlyList<ProcessInfo> Snapshot() => Processes.ToList();

    public ProcessInfo? TryGet(int pid) => Processes.FirstOrDefault(p => p.Pid == pid);

    public bool Kill(ProcessInfo target)
    {
        if (FailKill.Contains(target.Pid)) return false;
        Killed.Add(target.Pid);
        Processes.RemoveAll(p => p.Pid == target.Pid);
        return true;
    }

    /// <summary>Simulates EmptyWorkingSet: the working set drops to 10%.</summary>
    public bool TrimWorkingSet(int pid)
    {
        Trimmed.Add(pid);
        var i = Processes.FindIndex(p => p.Pid == pid);
        Processes[i] = Processes[i] with { WorkingSetBytes = Processes[i].WorkingSetBytes / 10 };
        return true;
    }
}
