using System.Management;

namespace CCRamCleaner.Core.Enforcement;

/// <summary>Near-instant process start notifications via WMI (requires administrator rights).</summary>
public sealed class ProcessStartWatcher : IDisposable
{
    private readonly ManagementEventWatcher _watcher;

    public ProcessStartWatcher(Action<int> onStarted)
    {
        _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += (_, e) => onStarted(Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value));
    }

    public void Start() => _watcher.Start();
    public void Stop() => _watcher.Stop();
    public void Dispose() => _watcher.Dispose();
}
