using System.Text.Json;
using System.Text.Json.Serialization;
using RamCleaner.Core.Services;

namespace RamCleaner.Core.State;

public sealed record ServiceJournalEntry(string Name, ServiceStartType OriginalStartType, bool OriginalDelayedAutoStart, bool WasRunning);

/// <summary>
/// Write-ahead record of every service we changed, persisted before the change is made,
/// so a crash or kill while active can be undone on the next start.
/// </summary>
public sealed class SessionJournal(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public string Path => path;
    public bool Exists => File.Exists(path);

    public IReadOnlyList<ServiceJournalEntry> Load()
    {
        lock (_gate) return LoadUnlocked();
    }

    /// <summary>Adds an entry unless the service is already recorded (the first original wins).</summary>
    public void Record(ServiceJournalEntry entry)
    {
        lock (_gate)
        {
            var entries = LoadUnlocked();
            if (entries.Any(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase))) return;
            entries.Add(entry);
            WriteUnlocked(entries);
        }
    }

    public void Replace(IReadOnlyList<ServiceJournalEntry> entries)
    {
        lock (_gate)
        {
            if (entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                WriteUnlocked(entries);
            }
        }
    }

    public void Clear() => Replace([]);

    private List<ServiceJournalEntry> LoadUnlocked()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ServiceJournalEntry>>(File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
            return [];
        }
    }

    private void WriteUnlocked(IReadOnlyList<ServiceJournalEntry> entries)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entries, Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
