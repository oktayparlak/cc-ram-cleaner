namespace RamCleaner.Core.Services;

/// <summary>Numeric values match System.ServiceProcess.ServiceStartMode and the SCM SERVICE_*_START constants.</summary>
public enum ServiceStartType { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4 }

public sealed record ServiceState(string Name, ServiceStartType StartType, bool DelayedAutoStart, bool IsRunning, int ProcessId);

public interface IServiceControl
{
    /// <summary>Null when the service is not installed.</summary>
    ServiceState? Query(string name);

    void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart);
    void Stop(string name);
    void Start(string name);
}
