namespace RamCleaner.Core.Processes;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    int SessionId,
    long WorkingSetBytes,
    int ParentPid,
    bool IsCritical,
    TimeSpan CpuTime);
