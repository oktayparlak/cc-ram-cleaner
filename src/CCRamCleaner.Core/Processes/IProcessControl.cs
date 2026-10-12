namespace CCRamCleaner.Core.Processes;

public interface IProcessControl
{
    int CurrentProcessId { get; }
    IReadOnlyList<ProcessInfo> Snapshot();
    ProcessInfo? TryGet(int pid);

    /// <summary>
    /// Terminates only this exact process (never its tree): if its pid now belongs to a different process
    /// (creation time differs) nothing is touched. Returns true when the target is no longer running,
    /// false if it could not be stopped.
    /// </summary>
    bool Kill(ProcessInfo target);

    bool TrimWorkingSet(int pid);
}
