namespace RamCleaner.Core.Processes;

public interface IProcessControl
{
    int CurrentProcessId { get; }
    IReadOnlyList<ProcessInfo> Snapshot();
    ProcessInfo? TryGet(int pid);

    /// <summary>Terminates only this process (never its tree). Returns false if it could not be stopped.</summary>
    bool Kill(int pid);

    bool TrimWorkingSet(int pid);
}
