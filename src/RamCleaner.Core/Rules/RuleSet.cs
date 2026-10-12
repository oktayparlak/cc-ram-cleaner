namespace RamCleaner.Core.Rules;

public sealed record BlockRule(string Name, string Description, bool EnabledByDefault = true);

public sealed class RuleSet
{
    public List<BlockRule> BlockedServices { get; init; } = [];
    public List<BlockRule> BlockedProcesses { get; init; } = [];
    public List<string> DevAllowedProcesses { get; init; } = [];
}
