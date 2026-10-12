using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CCRamCleaner.Core.Native;

namespace CCRamCleaner.Core.Processes;

public sealed class WindowsProcessControl : IProcessControl
{
    public int CurrentProcessId { get; } = Environment.ProcessId;

    public IReadOnlyList<ProcessInfo> Snapshot()
    {
        var parents = ReadParentMap();
        var windowOwners = ReadWindowOwners();
        var result = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result.Add(Describe(process, parents, windowOwners)); }
                catch (InvalidOperationException) { /* exited during enumeration */ }
            }
        }
        return result;
    }

    public ProcessInfo? TryGet(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return Describe(process, ReadParentMap(), ReadWindowOwners());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public bool Kill(ProcessInfo target)
    {
        // One handle for both the identity check and the termination, so the pid cannot change in between.
        var handle = Win32.OpenProcess(Win32.PROCESS_TERMINATE | Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.SYNCHRONIZE, false, target.Pid);
        if (handle == IntPtr.Zero)
            return Marshal.GetLastWin32Error() == Win32.ERROR_INVALID_PARAMETER; // no such process: already gone
        try
        {
            if (!Win32.GetProcessTimes(handle, out var created, out _, out _, out _)) return false;
            if (created != target.CreationTime) return true; // pid reused: the target is gone, leave the newcomer alone
            if (!Win32.TerminateProcess(handle, 1)) return false;
            return Win32.WaitForSingleObject(handle, 3000) == Win32.WAIT_OBJECT_0;
        }
        finally { Win32.CloseHandle(handle); }
    }

    public bool TrimWorkingSet(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_SET_QUOTA, false, pid);
        if (handle == IntPtr.Zero) return false;
        try { return Win32.EmptyWorkingSet(handle); }
        finally { Win32.CloseHandle(handle); }
    }

    private static ProcessInfo Describe(Process process, IReadOnlyDictionary<int, int> parents, IReadOnlySet<int> windowOwners)
    {
        var (critical, cpu, created) = QueryHandleInfo(process.Id);
        return new ProcessInfo(process.Id, process.ProcessName, process.SessionId, process.WorkingSet64,
            parents.GetValueOrDefault(process.Id), critical, cpu, created, windowOwners.Contains(process.Id));
    }

    /// <summary>Processes we cannot open (protected/PPL) are reported as critical so they are never touched.</summary>
    private static (bool Critical, TimeSpan Cpu, long CreationTime) QueryHandleInfo(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return (true, TimeSpan.Zero, 0);
        try
        {
            var critical = !Win32.IsProcessCritical(handle, out var isCritical) || isCritical;
            return Win32.GetProcessTimes(handle, out var created, out _, out var kernel, out var user)
                ? (critical, TimeSpan.FromTicks(kernel + user), created)
                : (critical, TimeSpan.Zero, 0);
        }
        finally { Win32.CloseHandle(handle); }
    }

    /// <summary>Pids owning a visible, titled top-level window.</summary>
    private static HashSet<int> ReadWindowOwners()
    {
        var owners = new HashSet<int>();
        Win32.EnumWindows((window, _) =>
        {
            if (Win32.IsWindowVisible(window) && Win32.GetWindowTextLengthW(window) > 0)
            {
                Win32.GetWindowThreadProcessId(window, out var pid);
                owners.Add(pid);
            }
            return true;
        }, IntPtr.Zero);
        return owners;
    }

    private static Dictionary<int, int> ReadParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (snapshot == Win32.InvalidHandle) return map;
        try
        {
            var entry = new Win32.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<Win32.PROCESSENTRY32W>() };
            for (var ok = Win32.Process32FirstW(snapshot, ref entry); ok; ok = Win32.Process32NextW(snapshot, ref entry))
                map[entry.th32ProcessID] = entry.th32ParentProcessID;
        }
        finally { Win32.CloseHandle(snapshot); }
        return map;
    }
}
