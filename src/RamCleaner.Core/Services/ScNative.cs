using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RamCleaner.Core.Services;

internal static class ScNative
{
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
    public const int SC_STATUS_PROCESS_INFO = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_DELAYED_AUTO_START_INFO { public int fDelayedAutostart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ChangeServiceConfigW(IntPtr service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out SERVICE_STATUS_PROCESS buffer, int bufferSize, out int bytesNeeded);

    public static T WithService<T>(string name, uint access, Func<IntPtr, T> action)
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenServiceW(scm, name, access);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return action(service); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
