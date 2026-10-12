using CCRamCleaner.Core.Config;
using CCRamCleaner.Core.Tests.TestSupport;

namespace CCRamCleaner.Core.Tests;

public class ConfigStoreTests
{
    private readonly ConfigStore _store = new(TempPaths.NewFile("config.json"));

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = _store.Load();
        Assert.Equal(GuardProfile.Balanced, config.Profile);
        Assert.True(config.TrimEnabled);
        Assert.Equal(60, config.TrimIntervalSeconds);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_WithCaseInsensitiveToggles()
    {
        var config = new UserConfig { Profile = GuardProfile.Aggressive, AllowedProcesses = ["notepad"], AutoStandbyPurge = true };
        config.RuleToggles[UserConfig.ServiceKey("DiagTrack")] = false;
        _store.Save(config);

        var loaded = _store.Load();

        Assert.Equal(GuardProfile.Aggressive, loaded.Profile);
        Assert.Equal(new[] { "notepad" }, loaded.AllowedProcesses);
        Assert.True(loaded.AutoStandbyPurge);
        Assert.False(loaded.RuleToggles["SERVICE:DIAGTRACK"]);
        Assert.Contains("\"Aggressive\"", File.ReadAllText(_store.Path));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults_AndKeepsBackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, "{ not json");

        var config = _store.Load();

        Assert.Equal(GuardProfile.Balanced, config.Profile);
        Assert.Equal("{ not json", File.ReadAllText(_store.Path + ".bak"));
    }

    [Fact]
    public void Load_UnknownProfile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, """{ "profile": "Turbo" }""");
        Assert.Equal(GuardProfile.Balanced, _store.Load().Profile);
    }

    [Fact]
    public void Load_ClampsOutOfRangeValues_AndReplacesNullLists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, """{ "trimIntervalSeconds": 0, "standbyPurgeThresholdPercent": 99, "allowedProcesses": null, "ruleToggles": null }""");

        var config = _store.Load();

        Assert.Equal(10, config.TrimIntervalSeconds);
        Assert.Equal(50, config.StandbyPurgeThresholdPercent);
        Assert.Empty(config.AllowedProcesses);
        Assert.Empty(config.RuleToggles);
    }
}
