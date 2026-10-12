using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RamCleaner.Core.Native;

namespace RamCleaner.Core.Processes;

public sealed class WindowsProcessControl : IProcessControl
{
    public int CurrentProcessId { get; } = Environment.ProcessId;

    public IReadOnlyList<ProcessInfo> Snapshot()
    {
        var parents = ReadParentMap();
        var result = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result.Add(Describe(process, parents)); }
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
            return Describe(process, ReadParentMap());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public bool Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: false);
            return process.WaitForExit(3000);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    public bool TrimWorkingSet(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_SET_QUOTA, false, pid);
        if (handle == IntPtr.Zero) return false;
        try { return Win32.EmptyWorkingSet(handle); }
        finally { Win32.CloseHandle(handle); }
    }

    private static ProcessInfo Describe(Process process, IReadOnlyDictionary<int, int> parents)
    {
        var (critical, cpu) = QueryHandleInfo(process.Id);
        return new ProcessInfo(process.Id, process.ProcessName, process.SessionId, process.WorkingSet64,
            parents.GetValueOrDefault(process.Id), critical, cpu);
    }

    /// <summary>Processes we cannot open (protected/PPL) are reported as critical so they are never touched.</summary>
    private static (bool Critical, TimeSpan Cpu) QueryHandleInfo(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return (true, TimeSpan.Zero);
        try
        {
            var critical = !Win32.IsProcessCritical(handle, out var isCritical) || isCritical;
            var cpu = Win32.GetProcessTimes(handle, out _, out _, out var kernel, out var user)
                ? TimeSpan.FromTicks(kernel + user)
                : TimeSpan.Zero;
            return (critical, cpu);
        }
        finally { Win32.CloseHandle(handle); }
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
