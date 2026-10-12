using System.Text.Json;
using System.Text.Json.Serialization;

namespace RamCleaner.Core.Config;

public sealed class ConfigStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path => path;

    public UserConfig Load()
    {
        if (!File.Exists(path)) return new UserConfig();
        try
        {
            return Normalize(JsonSerializer.Deserialize<UserConfig>(File.ReadAllText(path), Options) ?? new UserConfig());
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            return new UserConfig();
        }
    }

    public void Save(UserConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }

    private static UserConfig Normalize(UserConfig c)
    {
        c.AllowedProcesses ??= [];
        c.BlockedProcesses ??= [];
        c.BlockedServices ??= [];
        var toggles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in c.RuleToggles ?? new Dictionary<string, bool>()) toggles[key] = value;
        c.RuleToggles = toggles;
        c.TrimIntervalSeconds = Math.Clamp(c.TrimIntervalSeconds, 10, 3600);
        c.StandbyPurgeThresholdPercent = Math.Clamp(c.StandbyPurgeThresholdPercent, 1, 50);
        return c;
    }
}
