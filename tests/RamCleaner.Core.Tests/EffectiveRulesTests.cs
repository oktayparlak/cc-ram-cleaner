using RamCleaner.Core.Config;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests;

public class EffectiveRulesTests
{
    private static readonly RuleSet Defaults = new()
    {
        BlockedServices = [new("DiagTrack", "telemetry"), new("Spooler", "print", EnabledByDefault: false)],
        BlockedProcesses = [new("Spotify", "music"), new("Discord", "chat", EnabledByDefault: false)],
        DevAllowedProcesses = ["claude", "node"],
    };

    [Fact]
    public void Build_UsesEnabledByDefault()
    {
        var rules = EffectiveRules.Build(Defaults, new UserConfig());
        Assert.Equal(new[] { "DiagTrack" }, rules.BlockedServices);
        Assert.Equal(new[] { "Spotify" }, rules.DefaultBlockedProcesses);
        Assert.Equal(new[] { "claude", "node" }, rules.DevAllowedProcesses);
        Assert.Equal(GuardProfile.Balanced, rules.Profile);
    }

    [Fact]
    public void Build_RuleTogglesOverrideDefaults_CaseInsensitively()
    {
        var config = new UserConfig();
        config.RuleToggles["SERVICE:diagtrack"] = false;
        config.RuleToggles[UserConfig.ServiceKey("Spooler")] = true;
        config.RuleToggles[UserConfig.ProcessKey("Discord")] = true;

        var rules = EffectiveRules.Build(Defaults, config);

        Assert.Equal(new[] { "Spooler" }, rules.BlockedServices);
        Assert.Equal(new[] { "Spotify", "Discord" }, rules.DefaultBlockedProcesses);
    }

    [Fact]
    public void Build_AddsUserBlockedServices_WithoutDuplicates()
    {
        var config = new UserConfig { BlockedServices = ["diagtrack", "MyBloatSvc"] };
        var rules = EffectiveRules.Build(Defaults, config);
        Assert.Equal(new[] { "DiagTrack", "MyBloatSvc" }, rules.BlockedServices);
    }

    [Fact]
    public void Build_DropsUnsafeUserBlockPatterns()
    {
        var config = new UserConfig { BlockedProcesses = ["*", "ab*", "notepad"], AllowedProcesses = ["Spotify"], Profile = GuardProfile.Aggressive };
        var rules = EffectiveRules.Build(Defaults, config);
        Assert.Equal(new[] { "notepad" }, rules.UserBlockedProcesses);
        Assert.Equal(new[] { "Spotify" }, rules.UserAllowedProcesses);
        Assert.Equal(GuardProfile.Aggressive, rules.Profile);
    }
}
