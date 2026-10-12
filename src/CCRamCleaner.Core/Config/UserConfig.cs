namespace CCRamCleaner.Core.Config;

public enum GuardProfile { Balanced, Aggressive }

public sealed class UserConfig
{
    public GuardProfile Profile { get; set; } = GuardProfile.Balanced;
    public List<string> AllowedProcesses { get; set; } = [];
    public List<string> BlockedProcesses { get; set; } = [];
    public List<string> BlockedServices { get; set; } = [];

    /// <summary>Keys from <see cref="ServiceKey"/>/<see cref="ProcessKey"/>; value overrides BlockRule.EnabledByDefault.</summary>
    public Dictionary<string, bool> RuleToggles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TrimEnabled { get; set; } = true;
    public int TrimIntervalSeconds { get; set; } = 60;
    public bool AutoStandbyPurge { get; set; }
    public int StandbyPurgeThresholdPercent { get; set; } = 10;
    public bool StartWithWindows { get; set; }

    public static string ServiceKey(string name) => "service:" + name;
    public static string ProcessKey(string name) => "process:" + name;
}
