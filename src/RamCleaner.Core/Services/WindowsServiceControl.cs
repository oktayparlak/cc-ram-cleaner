using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RamCleaner.Core.Services;

public sealed class WindowsServiceControl : IServiceControl
{
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public ServiceState? Query(string name)
    {
        using var sc = new ServiceController(name);
        try
        {
            var status = sc.Status;
            var running = status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending);
            return new ServiceState(sc.ServiceName, (ServiceStartType)(int)sc.StartType, ReadDelayedAutoStart(sc.ServiceName), running, ReadProcessId(sc.ServiceName));
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorServiceDoesNotExist })
        {
            return null;
        }
    }

    public void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart) =>
        ScNative.WithService(name, ScNative.SERVICE_CHANGE_CONFIG, handle =>
        {
            if (!ScNative.ChangeServiceConfigW(handle, ScNative.SERVICE_NO_CHANGE, (uint)startType, ScNative.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (startType == ServiceStartType.Automatic)
            {
                var info = new ScNative.SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayedAutoStart ? 1 : 0 };
                if (!ScNative.ChangeServiceConfig2W(handle, ScNative.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return true;
        });

    public void Stop(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Stopped) return;
        if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop(stopDependentServices: false);
        sc.WaitForStatus(ServiceControllerStatus.Stopped, WaitTimeout);
    }

    public void Start(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
    }

    private static bool ReadDelayedAutoStart(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key?.GetValue("DelayedAutostart") is int value && value == 1;
    }

    private static int ReadProcessId(string name)
    {
        try
        {
            return ScNative.WithService(name, ScNative.SERVICE_QUERY_STATUS, handle =>
                ScNative.QueryServiceStatusEx(handle, ScNative.SC_STATUS_PROCESS_INFO, out var status,
                    Marshal.SizeOf<ScNative.SERVICE_STATUS_PROCESS>(), out _)
                    ? (int)status.dwProcessId
                    : 0);
        }
        catch (Win32Exception)
        {
            return 0;
        }
    }
}
