using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests;

public class DefaultRulesTests
{
    private readonly RuleSet _rules = DefaultRules.Load();

    [Fact]
    public void Load_ContainsCoreEntries()
    {
        Assert.Contains(_rules.BlockedServices, r => r.Name == "DiagTrack");
        Assert.Contains(_rules.BlockedProcesses, r => r.Name == "Spotify");
        foreach (var dev in new[] { "claude", "node", "chrome", "com.docker.backend", "Docker Desktop", "vmmemWSL", "qemu-system-x86_64", "emulator", "adb", "studio64", "WindowsTerminal", "codex", "codex-x86_64-pc-windows-msvc", "opencode" })
            Assert.True(NamePattern.MatchesAny(_rules.DevAllowedProcesses, dev), dev);
    }

    [Fact]
    public void NoBlockedProcessIsAlsoADevTool()
    {
        foreach (var rule in _rules.BlockedProcesses)
            Assert.False(NamePattern.MatchesAny(_rules.DevAllowedProcesses, rule.Name), rule.Name);
    }

    [Fact]
    public void AllBlockedProcessPatternsAreSafe()
    {
        foreach (var rule in _rules.BlockedProcesses)
            Assert.True(NamePattern.IsSafeBlockPattern(rule.Name), rule.Name);
    }

    [Fact]
    public void OptInRulesAreDisabledByDefault()
    {
        Assert.False(_rules.BlockedServices.Single(r => r.Name == "Spooler").EnabledByDefault);
        Assert.True(_rules.BlockedServices.Single(r => r.Name == "DiagTrack").EnabledByDefault);
    }
}
