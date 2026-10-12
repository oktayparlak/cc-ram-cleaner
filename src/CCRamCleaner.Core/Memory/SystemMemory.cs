using System.ComponentModel;
using System.Runtime.InteropServices;
using CCRamCleaner.Core.Native;

namespace CCRamCleaner.Core.Memory;

public static class SystemMemory
{
    public static MemoryStatus Read()
    {
        var status = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (!Win32.GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new MemoryStatus(status.ullTotalPhys, status.ullAvailPhys);
    }

    public static int ForegroundProcessId()
    {
        var window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        Win32.GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    /// <summary>Empties the standby (file cache) list. Requires administrator rights.</summary>
    public static bool PurgeStandbyList()
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return false;
        var command = Win32.MemoryPurgeStandbyList;
        return Win32.NtSetSystemInformation(Win32.SystemMemoryListInformation, ref command, sizeof(int)) == 0;
    }

    private static bool EnablePrivilege(string privilege)
    {
        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TOKEN_ADJUST_PRIVILEGES | Win32.TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!Win32.LookupPrivilegeValueW(null, privilege, out var luid)) return false;
            var tp = new Win32.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = Win32.SE_PRIVILEGE_ENABLED };
            return Win32.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                && Marshal.GetLastWin32Error() == 0;
        }
        finally { Win32.CloseHandle(token); }
    }
}
