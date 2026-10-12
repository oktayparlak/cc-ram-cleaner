namespace CCRamCleaner.Core.Enforcement;

public enum LogKind { Info, Warning, Killed, ServiceStopped, Restored }

public sealed record LogEntry(DateTime Time, LogKind Kind, string Message);

/// <summary>Thread-safe in-memory log (last 500 entries). Added is raised on the writer's thread.</summary>
public sealed class ActivityLog
{
    private const int Capacity = 500;
    private readonly Queue<LogEntry> _entries = new();
    private readonly object _gate = new();

    public event Action<LogEntry>? Added;

    public void Write(LogKind kind, string message)
    {
        var entry = new LogEntry(DateTime.Now, kind, message);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > Capacity) _entries.Dequeue();
        }
        Added?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }
}
