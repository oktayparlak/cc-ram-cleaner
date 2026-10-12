namespace CCRamCleaner.Core.Processes;

/// <param name="CreationTime">Raw FILETIME of process creation; with Pid it identifies a process across pid reuse.</param>
/// <param name="HasWindow">The process owns a visible, titled top-level window (likely the user's open work).</param>
public sealed record ProcessInfo(
    int Pid,
    string Name,
    int SessionId,
    long WorkingSetBytes,
    int ParentPid,
    bool IsCritical,
    TimeSpan CpuTime,
    long CreationTime = 0,
    bool HasWindow = false);
