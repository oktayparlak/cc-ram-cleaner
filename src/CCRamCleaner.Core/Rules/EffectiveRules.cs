using CCRamCleaner.Core.Config;

namespace CCRamCleaner.Core.Rules;

/// <summary>Defaults + user config flattened into the lists the classifier and guard consume.</summary>
public sealed class EffectiveRules
{
    public required GuardProfile Profile { get; init; }
    public required IReadOnlyList<string> BlockedServices { get; init; }
    public required IReadOnlyList<string> DefaultBlockedProcesses { get; init; }
    public required IReadOnlyList<string> DevAllowedProcesses { get; init; }
    public required IReadOnlyList<string> UserAllowedProcesses { get; init; }
    public required IReadOnlyList<string> UserBlockedProcesses { get; init; }

    public static EffectiveRules Build(RuleSet defaults, UserConfig config)
    {
        bool Enabled(string key, bool byDefault) =>
            config.RuleToggles.TryGetValue(key, out var value) ? value : byDefault;

        return new EffectiveRules
        {
            Profile = config.Profile,
            BlockedServices = defaults.BlockedServices
                .Where(r => Enabled(UserConfig.ServiceKey(r.Name), r.EnabledByDefault))
                .Select(r => r.Name)
                .Concat(config.BlockedServices)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DefaultBlockedProcesses = defaults.BlockedProcesses
                .Where(r => Enabled(UserConfig.ProcessKey(r.Name), r.EnabledByDefault))
                .Select(r => r.Name)
                .ToList(),
            DevAllowedProcesses = defaults.DevAllowedProcesses.ToList(),
            UserAllowedProcesses = config.AllowedProcesses.ToList(),
            UserBlockedProcesses = config.BlockedProcesses.Where(NamePattern.IsSafeBlockPattern).ToList(),
        };
    }
}
