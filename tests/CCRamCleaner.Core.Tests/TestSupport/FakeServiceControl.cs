using CCRamCleaner.Core.Services;

namespace CCRamCleaner.Core.Tests.TestSupport;

internal sealed class FakeServiceControl : IServiceControl
{
    public Dictionary<string, ServiceState> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailStop { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = [];

    public void Add(string name, ServiceStartType startType, bool running, bool delayed = false, int pid = 0) =>
        Services[name] = new ServiceState(name, startType, delayed, running, pid);

    public ServiceState? Query(string name) => Services.TryGetValue(name, out var s) ? s : null;

    public void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart)
    {
        Calls.Add($"config {name} {startType}");
        Services[name] = Services[name] with { StartType = startType, DelayedAutoStart = delayedAutoStart };
    }

    /// <summary>Runs inside Stop before the state changes — lets a test hold the guard mid-activation.</summary>
    public Action<string>? OnStop { get; set; }

    public void Stop(string name)
    {
        Calls.Add($"stop {name}");
        OnStop?.Invoke(name);
        if (FailStop.Contains(name)) throw new InvalidOperationException("dependent services are running");
        Services[name] = Services[name] with { IsRunning = false };
    }

    public void Start(string name)
    {
        Calls.Add($"start {name}");
        Services[name] = Services[name] with { IsRunning = true };
    }
}
