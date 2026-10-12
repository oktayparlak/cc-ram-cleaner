# RamCleaner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tek butonlu bir Windows masaüstü aracı: aktifken gereksiz servis/uygulamaları durdurur ve tekrar başlamalarını engeller, dev araçlarına dokunmaz; pasife alınınca servisleri eski haline getirir.

**Architecture:** UI'dan bağımsız, tamamen fake'lerle test edilen bir `RamCleaner.Core` kütüphanesi (kurallar, sınıflandırıcı, write-ahead journal, `Guard` durum makinesi, bellek araçları) + bunun üstünde ince bir WPF uygulaması (`RamCleaner.App`, yönetici yetkisiyle, tray ikonu). Win32/WMI'ye dokunan her şey `IServiceControl` / `IProcessControl` arayüzlerinin arkasında.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm 8.4.0, System.Management, System.ServiceProcess.ServiceController, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-12-ram-cleaner-design.md`

## Global Constraints

- Target framework: `net10.0-windows`; `Nullable` enabled; `LangVersion latest`; Core ve test projelerinde `TreatWarningsAsErrors=true`.
- Uygulama `requireAdministrator` manifest ile çalışır; assembly/süreç adı `RamCleaner`.
- Journal: `%ProgramData%\RamCleaner\session.json`. Kullanıcı ayarları: `%APPDATA%\RamCleaner\config.json`.
- İsim eşleştirme: büyük/küçük harf duyarsız, **kültürden bağımsız** (Türkçe `I/ı` sorunu), sondaki `.exe` yok sayılır, `*` joker karakteri.
- Sınıflandırma önceliği: Protected (sabit) > kullanıcı İzin > kullanıcı Engel > varsayılan dev beyaz listesi > varsayılan kara liste > Unknown.
- Asla öldürülmez: Protected isim listesi, `IsProcessCritical`, açılıp incelenemeyen süreçler, aracın kendisi ve üst süreç zinciri. Agresif profil Unknown süreçleri yalnızca session ≠ 0 ise öldürür.
- Süreç öldürme tek süreçtir (ağaç değil).
- Servisler her iki profilde de yalnızca kara listeyle yönetilir; journal yazılamazsa hiçbir değişiklik yapılmaz.
- Zamanlayıcılar: süreç taraması 2 sn, servis kontrolü 5 sn, bellek tick'i 10 sn; trim aralığı varsayılan 60 sn (10–3600); otomatik standby eşiği varsayılan %10 (1–50), iki otomatik temizlik arası en az 5 dk.
- Engelleme bildirimi aynı isim için en fazla 60 sn'de bir.
- Lisans MIT; UI dili İngilizce varsayılan + Türkçe (`L` sınıfı).

## Review Focus

1. **Türkçe yerel ayar (tr-TR) altında isim eşleştirme** — `"IDEA64"` ile `"idea64"` eşleşmeli; kullanıcının kendi makinesi Türkçe. → Task 1 testi.
2. **Aktivasyonun ortasında çökme / journal yazılamaması** — servisler bir sonraki açılışta eski haline dönmeli; journal yazılamıyorsa hiçbir servis değişmemeli. → Task 6 testleri.
3. **PID yeniden kullanımı** — öldürülemeyen PID'in başka isimle yeniden kullanılması, trimmer'da isim değişimi, üst süreç zincirinde döngü. → Task 4, 6, 7 testleri.
4. **Fazla geniş kullanıcı kalıbı** (`*`, `ab*`) — sessizce yok sayılmalı, her şeyi öldürmemeli. → Task 2 testleri.
5. **Aktifken servisin kaldırılması / bağımlı servis yüzünden durdurulamaması** — akış devam etmeli, deaktivasyon temiz kalmalı. → Task 6 testleri.

---

## File Map

```
RamCleaner.slnx
Directory.Build.props
.github/workflows/ci.yml
README.md, LICENSE
src/RamCleaner.Core/
  RamCleaner.Core.csproj
  Rules/NamePattern.cs, RuleSet.cs, DefaultRules.cs, rules.default.json, EffectiveRules.cs
  Config/UserConfig.cs, ConfigStore.cs
  Processes/ProcessInfo.cs, IProcessControl.cs, ProcessTree.cs, WindowsProcessControl.cs
  Classification/ProtectedProcesses.cs, ProcessClassifier.cs
  Services/IServiceControl.cs, WindowsServiceControl.cs, ScNative.cs
  State/SessionJournal.cs
  Enforcement/ActivityLog.cs, Guard.cs, ProcessStartWatcher.cs, GuardRunner.cs
  Memory/MemoryStatus.cs, StandbyPolicy.cs, ClaudeSessionCounter.cs, WorkingSetTrimmer.cs, SystemMemory.cs
  Native/Win32.cs
src/RamCleaner.App/
  RamCleaner.App.csproj, app.manifest, App.xaml(.cs), MainWindow.xaml(.cs)
  L.cs, MainViewModel.cs, Rows.cs, TrayIcon.cs, StartupTask.cs
tests/RamCleaner.Core.Tests/
  TestSupport/TempPaths.cs, TestRules.cs, FakeServiceControl.cs, FakeProcessControl.cs
  NamePatternTests.cs, DefaultRulesTests.cs, EffectiveRulesTests.cs, ConfigStoreTests.cs,
  ProcessClassifierTests.cs, SessionJournalTests.cs, GuardServiceTests.cs, GuardProcessTests.cs,
  MemoryTests.cs, WindowsIntegrationTests.cs
```

Tüm komutlar repo kökünden (`C:\javaWorld\ram-cleaner`) Git Bash'te çalıştırılır.

---

### Task 1: Solution iskeleti + NamePattern

**Files:**
- Create: `Directory.Build.props`, `RamCleaner.slnx`, `src/RamCleaner.Core/RamCleaner.Core.csproj`, `tests/RamCleaner.Core.Tests/RamCleaner.Core.Tests.csproj`
- Create: `src/RamCleaner.Core/Rules/NamePattern.cs`
- Test: `tests/RamCleaner.Core.Tests/NamePatternTests.cs`

**Interfaces:**
- Produces: `static class NamePattern { string Normalize(string); bool Matches(string pattern, string name); bool MatchesAny(IEnumerable<string> patterns, string name); bool IsSafeBlockPattern(string pattern); }` (namespace `RamCleaner.Core.Rules`)

- [ ] **Step 1: .NET 10 SDK kur**

Run: `winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements`
(UAC onayı istenir — kullanıcı onaylamalı.) Yeni shell'de `dotnet --version` → `10.0.x`. `dotnet` PATH'te yoksa `"/c/Program Files/dotnet/dotnet.exe"` kullan.

- [ ] **Step 2: Projeleri oluştur**

```bash
dotnet new sln -n RamCleaner --format slnx
dotnet new classlib -n RamCleaner.Core -o src/RamCleaner.Core
dotnet new xunit -n RamCleaner.Core.Tests -o tests/RamCleaner.Core.Tests
rm src/RamCleaner.Core/Class1.cs tests/RamCleaner.Core.Tests/UnitTest1.cs
sed -i '/<TargetFramework>/d;/<ImplicitUsings>/d;/<Nullable>/d' src/RamCleaner.Core/RamCleaner.Core.csproj tests/RamCleaner.Core.Tests/RamCleaner.Core.Tests.csproj
dotnet add tests/RamCleaner.Core.Tests reference src/RamCleaner.Core
dotnet sln RamCleaner.slnx add src/RamCleaner.Core tests/RamCleaner.Core.Tests
```

`Directory.Build.props` (repo kökü):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Vulnerability advisories on transitive packages should be visible, not break the build. -->
    <WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

`src/RamCleaner.Core/RamCleaner.Core.csproj` içeriğini tamamen şununla değiştir:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="System.Management" Version="10.0.0" />
    <PackageReference Include="System.ServiceProcess.ServiceController" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Rules\rules.default.json" LogicalName="RamCleaner.Core.Rules.rules.default.json" />
  </ItemGroup>
</Project>
```

Henüz `rules.default.json` yok; Task 2'ye kadar boş bir dosya koy: `mkdir -p src/RamCleaner.Core/Rules && echo '{}' > src/RamCleaner.Core/Rules/rules.default.json`.

- [ ] **Step 3: Failing test yaz** — `tests/RamCleaner.Core.Tests/NamePatternTests.cs`

```csharp
using System.Globalization;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests;

public class NamePatternTests
{
    [Theory]
    [InlineData("Spotify", "spotify", true)]
    [InlineData("Spotify", "Spotify.exe", true)]
    [InlineData("Spotify.exe", "Spotify", true)]
    [InlineData("com.docker.*", "com.docker.backend", true)]
    [InlineData("com.docker.*", "comXdocker.backend", false)]
    [InlineData("vmmem*", "vmmemWSL", true)]
    [InlineData("qemu-system-*", "qemu-system-x86_64", true)]
    [InlineData("node", "nodemon", false)]
    [InlineData("Docker Desktop", "docker desktop", true)]
    public void Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, NamePattern.Matches(pattern, name));

    [Fact]
    public void Matches_IsCultureInvariant_UnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.True(NamePattern.Matches("IDEA64", "idea64"));
            Assert.True(NamePattern.Matches("I*", "idea64"));
            Assert.True(NamePattern.Matches("MSBuild", "msbuild"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void MatchesAny_TrueWhenOnePatternMatches() =>
        Assert.True(NamePattern.MatchesAny(["chrome", "node"], "node.exe"));

    [Theory]
    [InlineData("", false)]
    [InlineData("*", false)]
    [InlineData("**", false)]
    [InlineData("a*", false)]
    [InlineData("ab*", false)]
    [InlineData("abc*", true)]
    [InlineData("Spotify", true)]
    public void IsSafeBlockPattern(string pattern, bool expected) =>
        Assert.Equal(expected, NamePattern.IsSafeBlockPattern(pattern));
}
```

- [ ] **Step 4: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~NamePatternTests"`
Expected: Build FAILED — `CS0103/CS0246: 'NamePattern' bulunamadı`.

- [ ] **Step 5: Implementasyon** — `src/RamCleaner.Core/Rules/NamePattern.cs`

```csharp
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RamCleaner.Core.Rules;

/// <summary>Case-insensitive, culture-invariant process/service name matching with '*' wildcards.</summary>
public static class NamePattern
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static string Normalize(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    public static bool Matches(string pattern, string name)
    {
        var p = Normalize(pattern);
        var n = Normalize(name);
        if (!p.Contains('*')) return string.Equals(p, n, StringComparison.OrdinalIgnoreCase);
        var regex = Cache.GetOrAdd(p, key => new Regex(
            "^" + Regex.Escape(key).Replace(@"\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        return regex.IsMatch(n);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string name) =>
        patterns.Any(p => Matches(p, name));

    /// <summary>Rejects block patterns too broad to apply safely ("*", "a*", empty).</summary>
    public static bool IsSafeBlockPattern(string pattern) =>
        Normalize(pattern).Replace("*", "").Length >= 3;
}
```

- [ ] **Step 6: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~NamePatternTests"`
Expected: PASS (tümü).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): solution scaffold and culture-invariant NamePattern"
```

---

### Task 2: Kural modeli, varsayılan kurallar, UserConfig, EffectiveRules

**Files:**
- Create: `src/RamCleaner.Core/Rules/RuleSet.cs`, `Rules/DefaultRules.cs`, `Rules/rules.default.json`, `Rules/EffectiveRules.cs`, `src/RamCleaner.Core/Config/UserConfig.cs`
- Test: `tests/RamCleaner.Core.Tests/DefaultRulesTests.cs`, `tests/RamCleaner.Core.Tests/EffectiveRulesTests.cs`

**Interfaces:**
- Consumes: `NamePattern` (Task 1)
- Produces:
  - `record BlockRule(string Name, string Description, bool EnabledByDefault = true)`; `class RuleSet { List<BlockRule> BlockedServices; List<BlockRule> BlockedProcesses; List<string> DevAllowedProcesses; }`; `static class DefaultRules { RuleSet Load(); }` (namespace `RamCleaner.Core.Rules`)
  - `enum GuardProfile { Balanced, Aggressive }`; `class UserConfig` (alanlar aşağıda) + `static string ServiceKey(string)`, `static string ProcessKey(string)` (namespace `RamCleaner.Core.Config`)
  - `class EffectiveRules { GuardProfile Profile; IReadOnlyList<string> BlockedServices, DefaultBlockedProcesses, DevAllowedProcesses, UserAllowedProcesses, UserBlockedProcesses; static EffectiveRules Build(RuleSet, UserConfig); }`

- [ ] **Step 1: Failing testler**

`tests/RamCleaner.Core.Tests/DefaultRulesTests.cs`:

```csharp
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
        foreach (var dev in new[] { "claude", "node", "chrome", "com.docker.backend", "Docker Desktop", "vmmemWSL", "qemu-system-x86_64", "emulator", "adb", "studio64", "WindowsTerminal" })
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
```

`tests/RamCleaner.Core.Tests/EffectiveRulesTests.cs`:

```csharp
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
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~DefaultRulesTests|FullyQualifiedName~EffectiveRulesTests"`
Expected: Build FAILED — `RuleSet`, `UserConfig`, `EffectiveRules` tanımlı değil.

- [ ] **Step 3: Implementasyon**

`src/RamCleaner.Core/Rules/RuleSet.cs`:

```csharp
namespace RamCleaner.Core.Rules;

public sealed record BlockRule(string Name, string Description, bool EnabledByDefault = true);

public sealed class RuleSet
{
    public List<BlockRule> BlockedServices { get; init; } = [];
    public List<BlockRule> BlockedProcesses { get; init; } = [];
    public List<string> DevAllowedProcesses { get; init; } = [];
}
```

`src/RamCleaner.Core/Rules/DefaultRules.cs`:

```csharp
using System.Text.Json;

namespace RamCleaner.Core.Rules;

public static class DefaultRules
{
    private const string ResourceName = "RamCleaner.Core.Rules.rules.default.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static RuleSet Load()
    {
        using var stream = typeof(DefaultRules).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize<RuleSet>(stream, Options)
            ?? throw new InvalidOperationException("rules.default.json is empty.");
    }
}
```

`src/RamCleaner.Core/Rules/rules.default.json` (Task 1'deki `{}` içeriğini değiştir):

```json
{
  "blockedServices": [
    { "name": "DiagTrack", "description": "Connected User Experiences and Telemetry" },
    { "name": "dmwappushservice", "description": "Device management WAP push (telemetry)" },
    { "name": "WerSvc", "description": "Windows Error Reporting" },
    { "name": "PcaSvc", "description": "Program Compatibility Assistant" },
    { "name": "WSearch", "description": "Windows Search indexer (Start menu file search gets slower)" },
    { "name": "SysMain", "description": "SysMain / Superfetch app prefetching" },
    { "name": "MapsBroker", "description": "Downloaded Maps Manager" },
    { "name": "lfsvc", "description": "Geolocation service" },
    { "name": "RetailDemo", "description": "Retail demo mode" },
    { "name": "Fax", "description": "Fax" },
    { "name": "WMPNetworkSvc", "description": "Windows Media Player network sharing" },
    { "name": "XblAuthManager", "description": "Xbox Live auth manager" },
    { "name": "XblGameSave", "description": "Xbox Live game save" },
    { "name": "XboxNetApiSvc", "description": "Xbox Live networking" },
    { "name": "XboxGipSvc", "description": "Xbox accessory management" },
    { "name": "edgeupdate", "description": "Microsoft Edge updater" },
    { "name": "edgeupdatem", "description": "Microsoft Edge updater (on demand)" },
    { "name": "gupdate", "description": "Google updater" },
    { "name": "gupdatem", "description": "Google updater (on demand)" },
    { "name": "HpTouchpointAnalyticsService", "description": "HP Insights Analytics" },
    { "name": "HPAppHelperCap", "description": "HP App Helper" },
    { "name": "HPDiagsCap", "description": "HP Diagnostics" },
    { "name": "HPSysInfoCap", "description": "HP System Info" },
    { "name": "HPNetworkCap", "description": "HP Network" },
    { "name": "AdobeARMservice", "description": "Adobe Acrobat updater" },
    { "name": "Steam Client Service", "description": "Steam client service" },
    { "name": "NVDisplay.ContainerLocalSystem", "description": "NVIDIA Display Container (control panel, overlay)", "enabledByDefault": false },
    { "name": "Spooler", "description": "Print Spooler (enable only if you never print)", "enabledByDefault": false }
  ],
  "blockedProcesses": [
    { "name": "Spotify", "description": "Spotify" },
    { "name": "Discord", "description": "Discord" },
    { "name": "steam", "description": "Steam" },
    { "name": "steamwebhelper", "description": "Steam web helper" },
    { "name": "EpicGamesLauncher", "description": "Epic Games Launcher" },
    { "name": "EpicWebHelper", "description": "Epic web helper" },
    { "name": "RiotClientServices", "description": "Riot Client" },
    { "name": "RiotClientUx", "description": "Riot Client UI" },
    { "name": "Battle.net", "description": "Battle.net" },
    { "name": "OneDrive", "description": "OneDrive sync" },
    { "name": "ms-teams", "description": "Microsoft Teams" },
    { "name": "Teams", "description": "Microsoft Teams (classic)" },
    { "name": "Skype", "description": "Skype" },
    { "name": "NVIDIA Overlay", "description": "NVIDIA overlay" },
    { "name": "nvsphelper64", "description": "NVIDIA ShadowPlay helper" },
    { "name": "OmenCommandCenterBackground", "description": "HP OMEN Gaming Hub background" },
    { "name": "PhoneExperienceHost", "description": "Phone Link" },
    { "name": "Widgets", "description": "Windows Widgets" },
    { "name": "WidgetService", "description": "Windows Widgets service" },
    { "name": "GameBar", "description": "Xbox Game Bar" },
    { "name": "GameBarFTServer", "description": "Xbox Game Bar helper" },
    { "name": "XboxPcApp", "description": "Xbox app" },
    { "name": "Copilot", "description": "Microsoft Copilot app" },
    { "name": "Creative Cloud", "description": "Adobe Creative Cloud" },
    { "name": "CCXProcess", "description": "Adobe Creative Cloud helper" },
    { "name": "Adobe Desktop Service", "description": "Adobe desktop service" },
    { "name": "AdobeCollabSync", "description": "Adobe Acrobat sync" },
    { "name": "iCUE", "description": "Corsair iCUE" },
    { "name": "Razer Synapse*", "description": "Razer Synapse" },
    { "name": "ArmouryCrate*", "description": "ASUS Armoury Crate" },
    { "name": "lghub*", "description": "Logitech G HUB" },
    { "name": "Overwolf", "description": "Overwolf" }
  ],
  "devAllowedProcesses": [
    "claude", "node", "bun", "deno", "python*", "uv", "java", "javaw", "dotnet", "MSBuild", "VBCSCompiler",
    "git", "git-remote-*", "ssh", "ssh-agent", "bash", "sh", "cmd", "powershell", "pwsh",
    "WindowsTerminal", "OpenConsole", "conhost",
    "wsl", "wslhost", "wslservice", "vmmem*", "vmwp", "Docker Desktop", "com.docker.*", "docker*",
    "chrome", "msedge", "msedgewebview2", "firefox",
    "Code", "Cursor", "Windsurf", "Zed", "idea64", "studio64", "rider64", "devenv",
    "qemu-system-*", "emulator*", "adb", "gradle*", "postman"
  ]
}
```

`src/RamCleaner.Core/Config/UserConfig.cs`:

```csharp
namespace RamCleaner.Core.Config;

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
```

`src/RamCleaner.Core/Rules/EffectiveRules.cs`:

```csharp
using RamCleaner.Core.Config;

namespace RamCleaner.Core.Rules;

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
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~DefaultRulesTests|FullyQualifiedName~EffectiveRulesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): rule model, default rules and effective rule builder"
```

---

### Task 3: ConfigStore

**Files:**
- Create: `src/RamCleaner.Core/Config/ConfigStore.cs`, `tests/RamCleaner.Core.Tests/TestSupport/TempPaths.cs`
- Test: `tests/RamCleaner.Core.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Consumes: `UserConfig`, `GuardProfile` (Task 2)
- Produces: `sealed class ConfigStore(string path) { string Path; UserConfig Load(); void Save(UserConfig); }`; test helper `static class TempPaths { string NewFile(string fileName); }`

- [ ] **Step 1: Failing testler**

`tests/RamCleaner.Core.Tests/TestSupport/TempPaths.cs`:

```csharp
namespace RamCleaner.Core.Tests.TestSupport;

internal static class TempPaths
{
    /// <summary>A path to a not-yet-existing file inside a fresh, unique temp directory.</summary>
    public static string NewFile(string fileName) =>
        Path.Combine(Path.GetTempPath(), "ramcleaner-tests", Guid.NewGuid().ToString("N"), fileName);
}
```

`tests/RamCleaner.Core.Tests/ConfigStoreTests.cs`:

```csharp
using RamCleaner.Core.Config;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

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
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: Build FAILED — `ConfigStore` tanımlı değil.

- [ ] **Step 3: Implementasyon** — `src/RamCleaner.Core/Config/ConfigStore.cs`

```csharp
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
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): JSON config store with corruption fallback and clamping"
```

---

### Task 4: Süreç modeli, koruma listesi, ProcessClassifier, ProcessTree

**Files:**
- Create: `src/RamCleaner.Core/Processes/ProcessInfo.cs`, `Processes/IProcessControl.cs`, `Processes/ProcessTree.cs`, `src/RamCleaner.Core/Classification/ProtectedProcesses.cs`, `Classification/ProcessClassifier.cs`, `tests/RamCleaner.Core.Tests/TestSupport/TestRules.cs`
- Test: `tests/RamCleaner.Core.Tests/ProcessClassifierTests.cs`

**Interfaces:**
- Consumes: `NamePattern`, `EffectiveRules`, `GuardProfile`, `DefaultRules`
- Produces:
  - `record ProcessInfo(int Pid, string Name, int SessionId, long WorkingSetBytes, int ParentPid, bool IsCritical, TimeSpan CpuTime)` (namespace `RamCleaner.Core.Processes`)
  - `interface IProcessControl { int CurrentProcessId { get; } IReadOnlyList<ProcessInfo> Snapshot(); ProcessInfo? TryGet(int pid); bool Kill(int pid); bool TrimWorkingSet(int pid); }`
  - `static class ProcessTree { HashSet<int> SelfAndAncestors(IReadOnlyList<ProcessInfo> snapshot, int pid); }`
  - `static class ProtectedProcesses { IReadOnlyList<string> Names; bool IsProtected(string name); }` (namespace `RamCleaner.Core.Classification`)
  - `enum Verdict { Protected, UserAllowed, UserBlocked, DevAllowed, DefaultBlocked, Unknown }`; `sealed class ProcessClassifier(EffectiveRules rules, IReadOnlySet<int> shieldedPids) { EffectiveRules Rules; Verdict Classify(ProcessInfo); bool ShouldKill(ProcessInfo); }`
  - Test helper `TestRules.Make(...)`

- [ ] **Step 1: Failing testler**

`tests/RamCleaner.Core.Tests/TestSupport/TestRules.cs`:

```csharp
using RamCleaner.Core.Config;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests.TestSupport;

internal static class TestRules
{
    public static EffectiveRules Make(
        GuardProfile profile = GuardProfile.Balanced,
        string[]? services = null,
        string[]? blocked = null,
        string[]? dev = null,
        string[]? userAllowed = null,
        string[]? userBlocked = null) => new()
    {
        Profile = profile,
        BlockedServices = services ?? [],
        DefaultBlockedProcesses = blocked ?? ["Spotify"],
        DevAllowedProcesses = dev ?? ["claude", "node", "chrome", "com.docker.*"],
        UserAllowedProcesses = userAllowed ?? [],
        UserBlockedProcesses = userBlocked ?? [],
    };
}
```

`tests/RamCleaner.Core.Tests/ProcessClassifierTests.cs`:

```csharp
using RamCleaner.Core.Classification;
using RamCleaner.Core.Config;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class ProcessClassifierTests
{
    private static ProcessInfo P(string name, int pid = 100, int session = 1, bool critical = false, int parent = 0) =>
        new(pid, name, session, 0, parent, critical, TimeSpan.Zero);

    private static ProcessClassifier C(EffectiveRules rules, params int[] shielded) => new(rules, shielded.ToHashSet());

    [Fact]
    public void Protected_BeatsUserBlock()
    {
        var c = C(TestRules.Make(userBlocked: ["explorer"]));
        Assert.Equal(Verdict.Protected, c.Classify(P("explorer")));
        Assert.False(c.ShouldKill(P("explorer")));
    }

    [Fact]
    public void UserAllowed_BeatsDefaultBlock()
    {
        var c = C(TestRules.Make(userAllowed: ["spotify"]));
        Assert.Equal(Verdict.UserAllowed, c.Classify(P("Spotify")));
        Assert.False(c.ShouldKill(P("Spotify")));
    }

    [Fact]
    public void UserBlocked_BeatsDevAllowed()
    {
        var c = C(TestRules.Make(userBlocked: ["chrome"]));
        Assert.Equal(Verdict.UserBlocked, c.Classify(P("chrome")));
        Assert.True(c.ShouldKill(P("chrome")));
    }

    [Fact]
    public void DefaultBlocked_IsKilled_DevToolIsNot()
    {
        var c = C(TestRules.Make());
        Assert.True(c.ShouldKill(P("Spotify")));
        Assert.Equal(Verdict.DevAllowed, c.Classify(P("com.docker.backend")));
        Assert.False(c.ShouldKill(P("claude")));
    }

    [Fact]
    public void Balanced_LeavesUnknownAlone()
    {
        var c = C(TestRules.Make(GuardProfile.Balanced));
        Assert.Equal(Verdict.Unknown, c.Classify(P("notepad")));
        Assert.False(c.ShouldKill(P("notepad")));
    }

    [Fact]
    public void Aggressive_KillsUnknownOnlyInUserSession()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive));
        Assert.True(c.ShouldKill(P("notepad", session: 1)));
        Assert.False(c.ShouldKill(P("SomeVendorService", session: 0)));
    }

    [Fact]
    public void CriticalSystemAndShieldedPids_AreProtected()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive), 777);
        Assert.Equal(Verdict.Protected, c.Classify(P("whatever", critical: true)));
        Assert.Equal(Verdict.Protected, c.Classify(P("System", pid: 4)));
        Assert.Equal(Verdict.Protected, c.Classify(P("MyLauncher", pid: 777)));
    }

    [Fact]
    public void Antivirus_IsProtected_EvenWhenUserBlocksIt()
    {
        var c = C(TestRules.Make(GuardProfile.Aggressive, userBlocked: ["avp"]));
        Assert.False(c.ShouldKill(P("avp")));
        Assert.False(c.ShouldKill(P("MsMpEng", session: 0)));
    }

    [Fact]
    public void NoDefaultBlockedProcessIsProtected()
    {
        foreach (var rule in DefaultRules.Load().BlockedProcesses)
            Assert.False(ProtectedProcesses.IsProtected(rule.Name), rule.Name);
    }

    [Fact]
    public void SelfAndAncestors_FollowsParentChain()
    {
        var snapshot = new[] { P("app", pid: 10, parent: 20), P("terminal", pid: 20, parent: 30), P("explorer", pid: 30, parent: 0), P("other", pid: 40, parent: 30) };
        Assert.Equal(new[] { 10, 20, 30 }, ProcessTree.SelfAndAncestors(snapshot, 10).Order());
    }

    [Fact]
    public void SelfAndAncestors_StopsOnPidReuseCycle()
    {
        var snapshot = new[] { P("a", pid: 5, parent: 6), P("b", pid: 6, parent: 5) };
        Assert.Equal(new[] { 5, 6 }, ProcessTree.SelfAndAncestors(snapshot, 5).Order());
    }
}
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~ProcessClassifierTests"`
Expected: Build FAILED — `ProcessInfo`, `ProcessClassifier` tanımlı değil.

- [ ] **Step 3: Implementasyon**

`src/RamCleaner.Core/Processes/ProcessInfo.cs`:

```csharp
namespace RamCleaner.Core.Processes;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    int SessionId,
    long WorkingSetBytes,
    int ParentPid,
    bool IsCritical,
    TimeSpan CpuTime);
```

`src/RamCleaner.Core/Processes/IProcessControl.cs`:

```csharp
namespace RamCleaner.Core.Processes;

public interface IProcessControl
{
    int CurrentProcessId { get; }
    IReadOnlyList<ProcessInfo> Snapshot();
    ProcessInfo? TryGet(int pid);

    /// <summary>Terminates only this process (never its tree). Returns false if it could not be stopped.</summary>
    bool Kill(int pid);

    bool TrimWorkingSet(int pid);
}
```

`src/RamCleaner.Core/Processes/ProcessTree.cs`:

```csharp
namespace RamCleaner.Core.Processes;

public static class ProcessTree
{
    /// <summary>The pid plus its parent chain. Stops on cycles caused by pid reuse.</summary>
    public static HashSet<int> SelfAndAncestors(IReadOnlyList<ProcessInfo> snapshot, int pid)
    {
        var parents = new Dictionary<int, int>();
        foreach (var p in snapshot) parents.TryAdd(p.Pid, p.ParentPid);

        var result = new HashSet<int>();
        var current = pid;
        while (result.Add(current) && parents.TryGetValue(current, out var parent) && parent != 0)
            current = parent;
        return result;
    }
}
```

`src/RamCleaner.Core/Classification/ProtectedProcesses.cs`:

```csharp
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Classification;

/// <summary>Hard-coded never-touch list. Users cannot remove entries.</summary>
public static class ProtectedProcesses
{
    public static readonly IReadOnlyList<string> Names =
    [
        // Windows core
        "System", "Idle", "Registry", "Secure System", "Memory Compression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "dwm", "explorer", "sihost", "fontdrvhost", "ctfmon", "audiodg",
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "TextInputHost", "ApplicationFrameHost",
        "RuntimeBroker", "taskhostw", "dllhost", "spoolsv", "WmiPrvSE", "WUDFHost", "dasHost", "LogonUI", "LockApp",
        "userinit", "consent", "CredentialUIBroker", "UserOOBEBroker", "SystemSettingsBroker", "Taskmgr",
        // Security software
        "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv", "MpDefenderCoreService", "smartscreen",
        "avp", "avpui", "ksde", "ksdeui", "ekrn", "egui", "bdagent", "vsserv", "AvastSvc", "AvastUI", "avgsvc", "AVGUI",
        // Ourselves
        "RamCleaner",
    ];

    public static bool IsProtected(string name) => NamePattern.MatchesAny(Names, name);
}
```

`src/RamCleaner.Core/Classification/ProcessClassifier.cs`:

```csharp
using RamCleaner.Core.Config;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Classification;

public enum Verdict { Protected, UserAllowed, UserBlocked, DevAllowed, DefaultBlocked, Unknown }

public sealed class ProcessClassifier(EffectiveRules rules, IReadOnlySet<int> shieldedPids)
{
    public EffectiveRules Rules => rules;

    public Verdict Classify(ProcessInfo p)
    {
        if (p.Pid is 0 or 4 || p.IsCritical || shieldedPids.Contains(p.Pid) || ProtectedProcesses.IsProtected(p.Name))
            return Verdict.Protected;
        if (NamePattern.MatchesAny(rules.UserAllowedProcesses, p.Name)) return Verdict.UserAllowed;
        if (NamePattern.MatchesAny(rules.UserBlockedProcesses, p.Name)) return Verdict.UserBlocked;
        if (NamePattern.MatchesAny(rules.DevAllowedProcesses, p.Name)) return Verdict.DevAllowed;
        if (NamePattern.MatchesAny(rules.DefaultBlockedProcesses, p.Name)) return Verdict.DefaultBlocked;
        return Verdict.Unknown;
    }

    public bool ShouldKill(ProcessInfo p) => Classify(p) switch
    {
        Verdict.UserBlocked or Verdict.DefaultBlocked => true,
        Verdict.Unknown => rules.Profile == GuardProfile.Aggressive && p.SessionId != 0,
        _ => false,
    };
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~ProcessClassifierTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): process model, protected list and classifier"
```

---

### Task 5: Servis soyutlaması + SessionJournal

**Files:**
- Create: `src/RamCleaner.Core/Services/IServiceControl.cs`, `src/RamCleaner.Core/State/SessionJournal.cs`
- Test: `tests/RamCleaner.Core.Tests/SessionJournalTests.cs`

**Interfaces:**
- Produces:
  - `enum ServiceStartType { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4 }`; `record ServiceState(string Name, ServiceStartType StartType, bool DelayedAutoStart, bool IsRunning, int ProcessId)`; `interface IServiceControl { ServiceState? Query(string name); void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart); void Stop(string name); void Start(string name); }` (namespace `RamCleaner.Core.Services`)
  - `record ServiceJournalEntry(string Name, ServiceStartType OriginalStartType, bool OriginalDelayedAutoStart, bool WasRunning)`; `sealed class SessionJournal(string path) { string Path; bool Exists; IReadOnlyList<ServiceJournalEntry> Load(); void Record(ServiceJournalEntry); void Replace(IReadOnlyList<ServiceJournalEntry>); void Clear(); }` (namespace `RamCleaner.Core.State`)

- [ ] **Step 1: Failing testler** — `tests/RamCleaner.Core.Tests/SessionJournalTests.cs`

```csharp
using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class SessionJournalTests
{
    private readonly string _path = TempPaths.NewFile("session.json");

    [Fact]
    public void Record_PersistsAcrossInstances()
    {
        new SessionJournal(_path).Record(new("DiagTrack", ServiceStartType.Automatic, true, true));

        var journal = new SessionJournal(_path);

        Assert.True(journal.Exists);
        Assert.Equal(new[] { new ServiceJournalEntry("DiagTrack", ServiceStartType.Automatic, true, true) }, journal.Load());
    }

    [Fact]
    public void Record_KeepsTheFirstOriginalForAService()
    {
        var journal = new SessionJournal(_path);
        journal.Record(new("DiagTrack", ServiceStartType.Automatic, false, true));
        journal.Record(new("diagtrack", ServiceStartType.Disabled, false, false));

        var entry = Assert.Single(journal.Load());
        Assert.Equal(ServiceStartType.Automatic, entry.OriginalStartType);
    }

    [Fact]
    public void Replace_WithEmpty_DeletesFile()
    {
        var journal = new SessionJournal(_path);
        journal.Record(new("Fax", ServiceStartType.Manual, false, false));
        journal.Clear();
        Assert.False(journal.Exists);
        Assert.Empty(journal.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty_AndKeepsCopy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "[ broken");

        Assert.Empty(new SessionJournal(_path).Load());
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public void Record_WhenDirectoryCannotBeCreated_Throws()
    {
        var blocker = TempPaths.NewFile("blocker");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "");
        var journal = new SessionJournal(Path.Combine(blocker, "session.json"));

        Assert.ThrowsAny<IOException>(() => journal.Record(new("Fax", ServiceStartType.Manual, false, false)));
    }
}
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~SessionJournalTests"`
Expected: Build FAILED — `SessionJournal` tanımlı değil.

- [ ] **Step 3: Implementasyon**

`src/RamCleaner.Core/Services/IServiceControl.cs`:

```csharp
namespace RamCleaner.Core.Services;

/// <summary>Numeric values match System.ServiceProcess.ServiceStartMode and the SCM SERVICE_*_START constants.</summary>
public enum ServiceStartType { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4 }

public sealed record ServiceState(string Name, ServiceStartType StartType, bool DelayedAutoStart, bool IsRunning, int ProcessId);

public interface IServiceControl
{
    /// <summary>Null when the service is not installed.</summary>
    ServiceState? Query(string name);

    void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart);
    void Stop(string name);
    void Start(string name);
}
```

`src/RamCleaner.Core/State/SessionJournal.cs`:

```csharp
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
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~SessionJournalTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): service abstraction and write-ahead session journal"
```

---

### Task 6: ActivityLog + Guard (servis yaşam döngüsü ve süreç engelleme)

**Files:**
- Create: `src/RamCleaner.Core/Enforcement/ActivityLog.cs`, `Enforcement/Guard.cs`, `tests/RamCleaner.Core.Tests/TestSupport/FakeServiceControl.cs`, `TestSupport/FakeProcessControl.cs`
- Test: `tests/RamCleaner.Core.Tests/GuardServiceTests.cs`, `tests/RamCleaner.Core.Tests/GuardProcessTests.cs`

**Interfaces:**
- Consumes: `IServiceControl`, `ServiceState`, `ServiceStartType` (Task 5); `SessionJournal`, `ServiceJournalEntry` (Task 5); `IProcessControl`, `ProcessInfo`, `ProcessTree` (Task 4); `ProcessClassifier`, `Verdict` (Task 4); `EffectiveRules`, `NamePattern`
- Produces (namespace `RamCleaner.Core.Enforcement`):
  - `enum LogKind { Info, Warning, Killed, ServiceStopped, Restored }`; `record LogEntry(DateTime Time, LogKind Kind, string Message)`; `sealed class ActivityLog { event Action<LogEntry>? Added; void Write(LogKind, string); IReadOnlyList<LogEntry> Snapshot(); }`
  - `record PreviewResult(IReadOnlyList<ProcessInfo> Processes, IReadOnlyList<ServiceState> Services) { long EstimatedBytes; }`; `record BlockedProcessEvent(string Name, Verdict Verdict)`; `sealed class JournalWriteException : Exception`
  - `sealed class Guard(IServiceControl, IProcessControl, SessionJournal, ActivityLog)` with `bool IsActive`, `long ReclaimedBytes`, `event Action<BlockedProcessEvent>? ProcessBlocked`, `PreviewResult Preview(EffectiveRules)`, `void Activate(EffectiveRules)`, `void UpdateRules(EffectiveRules)`, `void Deactivate()`, `int RecoverIfNeeded()`, `void EnforceProcesses()`, `void EnforceServices()`, `void OnProcessStarted(int pid)`, `void AddReclaimed(long bytes)`
  - Test fakes `FakeServiceControl`, `FakeProcessControl` (API aşağıda)

- [ ] **Step 1: Test fake'lerini yaz**

`tests/RamCleaner.Core.Tests/TestSupport/FakeServiceControl.cs`:

```csharp
using RamCleaner.Core.Services;

namespace RamCleaner.Core.Tests.TestSupport;

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

    public void Stop(string name)
    {
        Calls.Add($"stop {name}");
        if (FailStop.Contains(name)) throw new InvalidOperationException("dependent services are running");
        Services[name] = Services[name] with { IsRunning = false };
    }

    public void Start(string name)
    {
        Calls.Add($"start {name}");
        Services[name] = Services[name] with { IsRunning = true };
    }
}
```

`tests/RamCleaner.Core.Tests/TestSupport/FakeProcessControl.cs`:

```csharp
using RamCleaner.Core.Processes;

namespace RamCleaner.Core.Tests.TestSupport;

internal sealed class FakeProcessControl : IProcessControl
{
    public const long MB = 1024 * 1024;

    public List<ProcessInfo> Processes { get; } = [];
    public HashSet<int> FailKill { get; } = [];
    public List<int> Killed { get; } = [];
    public List<int> Trimmed { get; } = [];
    public int CurrentProcessId { get; set; } = 1;

    public ProcessInfo Add(int pid, string name, int session = 1, long mb = 100, int parent = 0, bool critical = false, double cpuSeconds = 0)
    {
        var p = new ProcessInfo(pid, name, session, mb * MB, parent, critical, TimeSpan.FromSeconds(cpuSeconds));
        Processes.Add(p);
        return p;
    }

    public void SetCpu(int pid, double seconds)
    {
        var i = Processes.FindIndex(p => p.Pid == pid);
        Processes[i] = Processes[i] with { CpuTime = TimeSpan.FromSeconds(seconds) };
    }

    public IReadOnlyList<ProcessInfo> Snapshot() => Processes.ToList();

    public ProcessInfo? TryGet(int pid) => Processes.FirstOrDefault(p => p.Pid == pid);

    public bool Kill(int pid)
    {
        if (FailKill.Contains(pid)) return false;
        Killed.Add(pid);
        Processes.RemoveAll(p => p.Pid == pid);
        return true;
    }

    /// <summary>Simulates EmptyWorkingSet: the working set drops to 10%.</summary>
    public bool TrimWorkingSet(int pid)
    {
        Trimmed.Add(pid);
        var i = Processes.FindIndex(p => p.Pid == pid);
        Processes[i] = Processes[i] with { WorkingSetBytes = Processes[i].WorkingSetBytes / 10 };
        return true;
    }
}
```

- [ ] **Step 2: Failing servis testleri** — `tests/RamCleaner.Core.Tests/GuardServiceTests.cs`

```csharp
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class GuardServiceTests
{
    private readonly FakeServiceControl _svc = new();
    private readonly FakeProcessControl _proc = new();
    private readonly SessionJournal _journal = new(TempPaths.NewFile("session.json"));
    private readonly ActivityLog _log = new();

    private Guard NewGuard(SessionJournal? journal = null) => new(_svc, _proc, journal ?? _journal, _log);

    [Fact]
    public void Activate_DisablesAndStopsRunningService_AndJournalsOriginal()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();

        guard.Activate(TestRules.Make(services: ["DiagTrack"]));

        Assert.True(guard.IsActive);
        Assert.Equal(ServiceStartType.Disabled, _svc.Query("DiagTrack")!.StartType);
        Assert.False(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(new[] { new ServiceJournalEntry("DiagTrack", ServiceStartType.Automatic, false, true) }, _journal.Load());
    }

    [Fact]
    public void Deactivate_RestoresStartType_AndRestartsOnlyPreviouslyRunning()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("WSearch", ServiceStartType.Manual, running: false);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack", "WSearch"]));

        guard.Deactivate();

        Assert.False(guard.IsActive);
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("DiagTrack")!.StartType);
        Assert.True(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(ServiceStartType.Manual, _svc.Query("WSearch")!.StartType);
        Assert.False(_svc.Query("WSearch")!.IsRunning);
        Assert.DoesNotContain("start WSearch", _svc.Calls);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void Deactivate_RestoresDelayedAutoStart()
    {
        _svc.Add("SysMain", ServiceStartType.Automatic, running: true, delayed: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["SysMain"]));
        guard.Deactivate();
        Assert.True(_svc.Query("SysMain")!.DelayedAutoStart);
    }

    [Fact]
    public void Activate_SkipsMissingAndAlreadyDisabledServices()
    {
        _svc.Add("Fax", ServiceStartType.Disabled, running: false);
        NewGuard().Activate(TestRules.Make(services: ["Fax", "NotInstalled"]));
        Assert.Empty(_svc.Calls);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void RecoverIfNeeded_RestoresServicesFromCrashedSession()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        NewGuard().Activate(TestRules.Make(services: ["DiagTrack"]));
        // simulated crash: the first guard is never deactivated

        var restarted = NewGuard();

        Assert.Equal(1, restarted.RecoverIfNeeded());
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("DiagTrack")!.StartType);
        Assert.True(_svc.Query("DiagTrack")!.IsRunning);
        Assert.False(_journal.Exists);
        Assert.Equal(0, restarted.RecoverIfNeeded());
    }

    [Fact]
    public void Activate_WhenJournalCannotBeWritten_ThrowsAndChangesNothing()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var blocker = TempPaths.NewFile("blocker");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "");
        var guard = NewGuard(new SessionJournal(Path.Combine(blocker, "session.json")));

        Assert.Throws<JournalWriteException>(() => guard.Activate(TestRules.Make(services: ["DiagTrack"])));

        Assert.Empty(_svc.Calls);
        Assert.False(guard.IsActive);
    }

    [Fact]
    public void Activate_WhenStopFails_LogsWarning_AndDeactivateStillRestores()
    {
        _svc.Add("Spooler", ServiceStartType.Automatic, running: true);
        _svc.FailStop.Add("Spooler");
        var guard = NewGuard();

        guard.Activate(TestRules.Make(services: ["Spooler"]));

        Assert.Contains(_log.Snapshot(), e => e.Kind == LogKind.Warning && e.Message.Contains("Spooler"));
        guard.Deactivate();
        Assert.Equal(ServiceStartType.Automatic, _svc.Query("Spooler")!.StartType);
        Assert.False(_journal.Exists);
    }

    [Fact]
    public void EnforceServices_StopsServiceThatWasRestartedWhileActive()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack"]));
        _svc.Services["DiagTrack"] = _svc.Services["DiagTrack"] with { IsRunning = true, StartType = ServiceStartType.Manual };

        guard.EnforceServices();

        Assert.False(_svc.Query("DiagTrack")!.IsRunning);
        Assert.Equal(ServiceStartType.Disabled, _svc.Query("DiagTrack")!.StartType);
        Assert.Equal(ServiceStartType.Automatic, Assert.Single(_journal.Load()).OriginalStartType);
    }

    [Fact]
    public void UpdateRules_KeepsOriginalJournalEntry()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        var rules = TestRules.Make(services: ["DiagTrack"]);
        guard.Activate(rules);

        guard.UpdateRules(rules);

        Assert.Equal(ServiceStartType.Automatic, Assert.Single(_journal.Load()).OriginalStartType);
    }

    [Fact]
    public void UpdateRules_RestoresServiceRemovedFromBlockList()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("WSearch", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack", "WSearch"]));

        guard.UpdateRules(TestRules.Make(services: ["DiagTrack"]));

        Assert.Equal(ServiceStartType.Automatic, _svc.Query("WSearch")!.StartType);
        Assert.True(_svc.Query("WSearch")!.IsRunning);
        Assert.Equal("DiagTrack", Assert.Single(_journal.Load()).Name);
    }

    [Fact]
    public void Deactivate_WhenServiceWasUninstalledMeanwhile_DropsJournalEntry()
    {
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        var guard = NewGuard();
        guard.Activate(TestRules.Make(services: ["DiagTrack"]));
        _svc.Services.Remove("DiagTrack");

        guard.Deactivate();

        Assert.False(_journal.Exists);
    }
}
```

- [ ] **Step 3: Failing süreç testleri** — `tests/RamCleaner.Core.Tests/GuardProcessTests.cs`

```csharp
using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class GuardProcessTests
{
    private readonly FakeServiceControl _svc = new();
    private readonly FakeProcessControl _proc = new();
    private readonly ActivityLog _log = new();
    private readonly Guard _guard;
    private readonly List<BlockedProcessEvent> _events = [];

    public GuardProcessTests()
    {
        _guard = new Guard(_svc, _proc, new SessionJournal(TempPaths.NewFile("session.json")), _log);
        _guard.ProcessBlocked += _events.Add;
    }

    [Fact]
    public void Activate_KillsBlockedProcesses_AndKeepsDevTools()
    {
        _proc.Add(10, "Spotify", mb: 600);
        _proc.Add(11, "claude");
        _proc.Add(12, "chrome");
        _proc.Add(13, "com.docker.backend");

        _guard.Activate(TestRules.Make());

        Assert.Equal(new[] { 10 }, _proc.Killed);
        Assert.Equal(600 * FakeProcessControl.MB, _guard.ReclaimedBytes);
    }

    [Fact]
    public void InitialSweep_DoesNotNotify_ButRelaunchDoes()
    {
        _proc.Add(10, "Spotify");
        _guard.Activate(TestRules.Make());
        Assert.Empty(_events);

        _proc.Add(20, "Spotify");
        _guard.OnProcessStarted(20);

        Assert.Contains(20, _proc.Killed);
        Assert.Equal("Spotify", Assert.Single(_events).Name);
    }

    [Fact]
    public void Aggressive_KillsUnknownUserSessionProcesses_Only()
    {
        _proc.Add(30, "notepad", session: 1);
        _proc.Add(31, "VendorAgent", session: 0);
        _proc.Add(32, "explorer", session: 1);
        _proc.Add(33, "claude", session: 1);
        _proc.Add(34, "weird", session: 1, critical: true);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        Assert.Equal(new[] { 30 }, _proc.Killed);
    }

    [Fact]
    public void Aggressive_NeverKillsOwnProcessOrItsAncestors()
    {
        _proc.CurrentProcessId = 40;
        _proc.Add(40, "RamCleanerDev", parent: 41);
        _proc.Add(41, "SomeLauncher", parent: 0);

        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void UpdateRules_AllowingAProcess_StopsKillingIt()
    {
        _guard.Activate(TestRules.Make(GuardProfile.Aggressive));
        _guard.UpdateRules(TestRules.Make(GuardProfile.Aggressive, userAllowed: ["notepad"]));

        _proc.Add(50, "notepad");
        _guard.OnProcessStarted(50);

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void UnkillableProcess_IsNotRetried_ButReusedPidWithOtherNameIs()
    {
        _proc.Add(60, "Spotify");
        _proc.FailKill.Add(60);
        var rules = TestRules.Make(blocked: ["Spotify", "Discord"]);
        _guard.Activate(rules);
        _guard.EnforceProcesses();
        _guard.EnforceProcesses();

        Assert.Single(_log.Snapshot(), e => e.Kind == LogKind.Warning && e.Message.Contains("(60)"));

        _proc.Processes.Clear();
        _proc.FailKill.Clear();
        _proc.Add(60, "Discord");
        _guard.EnforceProcesses();

        Assert.Contains(60, _proc.Killed);
    }

    [Fact]
    public void InactiveGuard_DoesNotKill()
    {
        _guard.Activate(TestRules.Make());
        _guard.Deactivate();

        _proc.Add(70, "Spotify");
        _guard.OnProcessStarted(70);
        _guard.EnforceProcesses();

        Assert.Empty(_proc.Killed);
    }

    [Fact]
    public void Preview_ListsTargets_WithoutChangingAnything()
    {
        _proc.Add(80, "Spotify", mb: 600);
        _proc.Add(81, "claude");
        _svc.Add("DiagTrack", ServiceStartType.Automatic, running: true);
        _svc.Add("Fax", ServiceStartType.Disabled, running: false);

        var preview = _guard.Preview(TestRules.Make(services: ["DiagTrack", "Fax"]));

        Assert.Equal(80, Assert.Single(preview.Processes).Pid);
        Assert.Equal("DiagTrack", Assert.Single(preview.Services).Name);
        Assert.Equal(600 * FakeProcessControl.MB, preview.EstimatedBytes);
        Assert.Empty(_proc.Killed);
        Assert.Empty(_svc.Calls);
        Assert.False(_guard.IsActive);
    }
}
```

- [ ] **Step 4: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~GuardServiceTests|FullyQualifiedName~GuardProcessTests"`
Expected: Build FAILED — `Guard`, `ActivityLog`, `JournalWriteException` tanımlı değil.

- [ ] **Step 5: Implementasyon**

`src/RamCleaner.Core/Enforcement/ActivityLog.cs`:

```csharp
namespace RamCleaner.Core.Enforcement;

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
```

`src/RamCleaner.Core/Enforcement/Guard.cs`:

```csharp
using RamCleaner.Core.Classification;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;

namespace RamCleaner.Core.Enforcement;

public sealed record PreviewResult(IReadOnlyList<ProcessInfo> Processes, IReadOnlyList<ServiceState> Services)
{
    public long EstimatedBytes => Processes.Sum(p => p.WorkingSetBytes);
}

public sealed record BlockedProcessEvent(string Name, Verdict Verdict);

public sealed class JournalWriteException(Exception inner)
    : Exception("Session journal could not be written: " + inner.Message, inner);

/// <summary>
/// The active/inactive state machine. All public members are serialized by one lock;
/// ProcessBlocked is raised under that lock, so handlers must not block (marshal with InvokeAsync).
/// </summary>
public sealed class Guard(IServiceControl services, IProcessControl processes, SessionJournal journal, ActivityLog log)
{
    private readonly object _gate = new();
    private readonly HashSet<(int Pid, string Name)> _unkillable = [];
    private readonly HashSet<string> _warnedServices = new(StringComparer.OrdinalIgnoreCase);
    private ProcessClassifier? _classifier;
    private long _reclaimed;

    public event Action<BlockedProcessEvent>? ProcessBlocked;

    public bool IsActive
    {
        get { lock (_gate) return _classifier != null; }
    }

    public long ReclaimedBytes => Interlocked.Read(ref _reclaimed);

    public void AddReclaimed(long bytes) => Interlocked.Add(ref _reclaimed, Math.Max(0, bytes));

    public PreviewResult Preview(EffectiveRules rules)
    {
        var snapshot = processes.Snapshot();
        var classifier = CreateClassifier(rules, snapshot);
        var targets = snapshot.Where(classifier.ShouldKill).OrderByDescending(p => p.WorkingSetBytes).ToList();
        var targetServices = rules.BlockedServices
            .Select(TryQuery)
            .OfType<ServiceState>()
            .Where(s => s.IsRunning || s.StartType != ServiceStartType.Disabled)
            .ToList();
        return new PreviewResult(targets, targetServices);
    }

    public void Activate(EffectiveRules rules)
    {
        lock (_gate)
        {
            if (_classifier != null) return;
            Interlocked.Exchange(ref _reclaimed, 0);
            _unkillable.Clear();
            _warnedServices.Clear();
            try
            {
                foreach (var name in rules.BlockedServices) DisableService(name);
            }
            catch (JournalWriteException)
            {
                RestoreServices(_ => true);
                throw;
            }
            _classifier = CreateClassifier(rules, processes.Snapshot());
            log.Write(LogKind.Info, $"Guard activated ({rules.Profile})");
            SweepProcesses(notify: false);
        }
    }

    public void UpdateRules(EffectiveRules rules)
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            _classifier = CreateClassifier(rules, processes.Snapshot());
            var blocked = new HashSet<string>(rules.BlockedServices, StringComparer.OrdinalIgnoreCase);
            RestoreServices(e => !blocked.Contains(e.Name));
            try
            {
                foreach (var name in rules.BlockedServices) DisableService(name);
            }
            catch (JournalWriteException ex)
            {
                log.Write(LogKind.Warning, ex.Message);
            }
            SweepProcesses(notify: false);
        }
    }

    public void Deactivate()
    {
        lock (_gate)
        {
            if (_classifier == null && !journal.Exists) return;
            _classifier = null;
            RestoreServices(_ => true);
            log.Write(LogKind.Info, "Guard deactivated");
        }
    }

    /// <summary>Call once at startup: undoes service changes left behind by a crashed session.</summary>
    public int RecoverIfNeeded()
    {
        lock (_gate)
        {
            if (_classifier != null || !journal.Exists) return 0;
            var count = journal.Load().Count;
            RestoreServices(_ => true);
            log.Write(LogKind.Restored, $"Recovered {count} service(s) from a previous session");
            return count;
        }
    }

    public void EnforceProcesses()
    {
        lock (_gate)
        {
            if (_classifier != null) SweepProcesses(notify: true);
        }
    }

    public void OnProcessStarted(int pid)
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            if (processes.TryGet(pid) is { } p) TryKill(p, notify: true);
        }
    }

    public void EnforceServices()
    {
        lock (_gate)
        {
            if (_classifier == null) return;
            foreach (var entry in journal.Load())
            {
                try
                {
                    var state = services.Query(entry.Name);
                    if (state is null) continue;
                    if (state.StartType != ServiceStartType.Disabled)
                        services.SetStartType(entry.Name, ServiceStartType.Disabled, false);
                    if (state.IsRunning)
                    {
                        services.Stop(entry.Name);
                        log.Write(LogKind.ServiceStopped, $"Service {entry.Name} was restarted — stopped again");
                    }
                }
                catch (Exception ex)
                {
                    if (_warnedServices.Add(entry.Name))
                        log.Write(LogKind.Warning, $"Service {entry.Name}: {ex.Message}");
                }
            }
        }
    }

    private void DisableService(string name)
    {
        var state = TryQuery(name);
        if (state is null || (state.StartType == ServiceStartType.Disabled && !state.IsRunning)) return;

        try
        {
            journal.Record(new ServiceJournalEntry(state.Name, state.StartType, state.DelayedAutoStart, state.IsRunning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JournalWriteException(ex);
        }

        try
        {
            services.SetStartType(state.Name, ServiceStartType.Disabled, false);
            if (state.IsRunning)
            {
                var bytes = OwnProcessBytes(state);
                services.Stop(state.Name);
                AddReclaimed(bytes);
            }
            log.Write(LogKind.ServiceStopped, $"Service {state.Name} disabled{(state.IsRunning ? " and stopped" : "")}");
        }
        catch (Exception ex)
        {
            log.Write(LogKind.Warning, $"Service {state.Name}: {ex.Message}");
        }
    }

    private void RestoreServices(Func<ServiceJournalEntry, bool> shouldRestore)
    {
        var keep = new List<ServiceJournalEntry>();
        foreach (var entry in journal.Load())
        {
            if (!shouldRestore(entry))
            {
                keep.Add(entry);
                continue;
            }
            try
            {
                if (services.Query(entry.Name) is null) continue; // uninstalled meanwhile
                services.SetStartType(entry.Name, entry.OriginalStartType, entry.OriginalDelayedAutoStart);
                if (entry.WasRunning && entry.OriginalStartType != ServiceStartType.Disabled
                    && services.Query(entry.Name) is { IsRunning: false })
                {
                    services.Start(entry.Name);
                }
                log.Write(LogKind.Restored, $"Service {entry.Name} restored ({entry.OriginalStartType})");
            }
            catch (Exception ex)
            {
                keep.Add(entry);
                log.Write(LogKind.Warning, $"Could not restore service {entry.Name}: {ex.Message}");
            }
        }
        journal.Replace(keep);
    }

    private void SweepProcesses(bool notify)
    {
        foreach (var p in processes.Snapshot()) TryKill(p, notify);
    }

    private void TryKill(ProcessInfo p, bool notify)
    {
        var classifier = _classifier!;
        if (!classifier.ShouldKill(p) || _unkillable.Contains((p.Pid, p.Name))) return;

        if (processes.Kill(p.Pid))
        {
            AddReclaimed(p.WorkingSetBytes);
            log.Write(LogKind.Killed, $"{p.Name} ({p.Pid}) stopped — {p.WorkingSetBytes / (1024 * 1024)} MB");
            if (notify) ProcessBlocked?.Invoke(new BlockedProcessEvent(p.Name, classifier.Classify(p)));
        }
        else
        {
            _unkillable.Add((p.Pid, p.Name));
            log.Write(LogKind.Warning, $"Could not stop {p.Name} ({p.Pid})");
        }
    }

    private long OwnProcessBytes(ServiceState state)
    {
        if (state.ProcessId == 0) return 0;
        var p = processes.TryGet(state.ProcessId);
        return p is null || NamePattern.Matches("svchost", p.Name) ? 0 : p.WorkingSetBytes;
    }

    private ServiceState? TryQuery(string name)
    {
        try
        {
            return services.Query(name);
        }
        catch (Exception ex)
        {
            log.Write(LogKind.Warning, $"Cannot query service {name}: {ex.Message}");
            return null;
        }
    }

    private ProcessClassifier CreateClassifier(EffectiveRules rules, IReadOnlyList<ProcessInfo> snapshot) =>
        new(rules, ProcessTree.SelfAndAncestors(snapshot, processes.CurrentProcessId));
}
```

- [ ] **Step 6: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~GuardServiceTests|FullyQualifiedName~GuardProcessTests"`
Expected: PASS. Ardından tüm suite: `dotnet test tests/RamCleaner.Core.Tests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): Guard state machine with journaled service changes and process blocking"
```

---

### Task 7: Bellek yardımcıları (trimmer, standby politikası, Claude oturum sayacı)

**Files:**
- Create: `src/RamCleaner.Core/Memory/MemoryStatus.cs`, `Memory/StandbyPolicy.cs`, `Memory/ClaudeSessionCounter.cs`, `Memory/WorkingSetTrimmer.cs`
- Test: `tests/RamCleaner.Core.Tests/MemoryTests.cs`

**Interfaces:**
- Consumes: `IProcessControl`, `ProcessInfo`, `ProtectedProcesses`, `NamePattern`, `FakeProcessControl` (test)
- Produces (namespace `RamCleaner.Core.Memory`):
  - `record MemoryStatus(ulong TotalBytes, ulong AvailableBytes) { double UsedPercent; double AvailablePercent; }`
  - `static class StandbyPolicy { TimeSpan MinInterval; bool ShouldPurge(MemoryStatus, int thresholdPercent, DateTime? lastPurge, DateTime now); }`
  - `static class ClaudeSessionCounter { int Count(IReadOnlyList<ProcessInfo>); }`
  - `record TrimResult(int ProcessCount, long WorkingSetReducedBytes)`; `sealed class WorkingSetTrimmer(IProcessControl, Func<int> foregroundPid) { TrimResult TrimIdle(); }`

- [ ] **Step 1: Failing testler** — `tests/RamCleaner.Core.Tests/MemoryTests.cs`

```csharp
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class MemoryTests
{
    private const ulong GB = 1024UL * 1024 * 1024;

    [Fact]
    public void MemoryStatus_ComputesPercentages()
    {
        var s = new MemoryStatus(16 * GB, 4 * GB);
        Assert.Equal(75, s.UsedPercent, 3);
        Assert.Equal(25, s.AvailablePercent, 3);
    }

    [Theory]
    [InlineData(1.0, null, true)]      // 6.25% available < 10, never purged
    [InlineData(1.0, 4.0, false)]      // purged 4 min ago
    [InlineData(1.0, 6.0, true)]       // purged 6 min ago
    [InlineData(4.0, null, false)]     // 25% available
    public void StandbyPolicy_ShouldPurge(double availableGb, double? minutesSinceLastPurge, bool expected)
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0);
        DateTime? last = minutesSinceLastPurge is { } m ? now.AddMinutes(-m) : null;
        var status = new MemoryStatus(16 * GB, (ulong)(availableGb * GB));
        Assert.Equal(expected, StandbyPolicy.ShouldPurge(status, 10, last, now));
    }

    [Fact]
    public void ClaudeSessionCounter_CountsOnlyTopLevelClaudeProcesses()
    {
        ProcessInfo P(int pid, string name, int parent) => new(pid, name, 1, 0, parent, false, TimeSpan.Zero);
        var snapshot = new[] { P(1, "bash", 0), P(2, "claude", 1), P(3, "claude", 2), P(4, "claude.exe", 1), P(5, "node", 4) };
        Assert.Equal(2, ClaudeSessionCounter.Count(snapshot));
    }

    [Fact]
    public void Trimmer_FirstPassTrimsNothing_SecondPassTrimsOnlyIdle()
    {
        var proc = new FakeProcessControl();
        proc.Add(10, "notepad", mb: 100, cpuSeconds: 1.0);
        proc.Add(11, "busyapp", mb: 100, cpuSeconds: 1.0);
        var trimmer = new WorkingSetTrimmer(proc, () => 0);

        Assert.Equal(0, trimmer.TrimIdle().ProcessCount);

        proc.SetCpu(10, 1.01);
        proc.SetCpu(11, 3.0);
        var result = trimmer.TrimIdle();

        Assert.Equal(new[] { 10 }, proc.Trimmed);
        Assert.Equal(1, result.ProcessCount);
        Assert.Equal(90 * FakeProcessControl.MB, result.WorkingSetReducedBytes);
    }

    [Fact]
    public void Trimmer_SkipsForegroundSelfVmmemProtectedAndCritical()
    {
        var proc = new FakeProcessControl { CurrentProcessId = 1 };
        proc.Add(1, "RamCleanerDev");
        proc.Add(2, "Code");          // foreground
        proc.Add(3, "vmmemWSL");
        proc.Add(4, "explorer");
        proc.Add(5, "weird", critical: true);
        var trimmer = new WorkingSetTrimmer(proc, () => 2);

        trimmer.TrimIdle();
        trimmer.TrimIdle();

        Assert.Empty(proc.Trimmed);
    }

    [Fact]
    public void Trimmer_DoesNotTrimWhenPidWasReusedByAnotherProcess()
    {
        var proc = new FakeProcessControl();
        proc.Add(10, "notepad");
        var trimmer = new WorkingSetTrimmer(proc, () => 0);
        trimmer.TrimIdle();

        proc.Processes.Clear();
        proc.Add(10, "mspaint");
        trimmer.TrimIdle();

        Assert.Empty(proc.Trimmed);
    }
}
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~MemoryTests"`
Expected: Build FAILED — `MemoryStatus` vb. tanımlı değil.

- [ ] **Step 3: Implementasyon**

`src/RamCleaner.Core/Memory/MemoryStatus.cs`:

```csharp
namespace RamCleaner.Core.Memory;

public sealed record MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
{
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - AvailableBytes) / TotalBytes;
    public double AvailablePercent => 100.0 - UsedPercent;
}
```

`src/RamCleaner.Core/Memory/StandbyPolicy.cs`:

```csharp
namespace RamCleaner.Core.Memory;

public static class StandbyPolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

    public static bool ShouldPurge(MemoryStatus status, int thresholdPercent, DateTime? lastPurge, DateTime now) =>
        status.AvailablePercent < thresholdPercent && (lastPurge is null || now - lastPurge.Value >= MinInterval);
}
```

`src/RamCleaner.Core/Memory/ClaudeSessionCounter.cs`:

```csharp
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Memory;

public static class ClaudeSessionCounter
{
    /// <summary>Counts claude processes whose parent is not itself a claude process (one per terminal session).</summary>
    public static int Count(IReadOnlyList<ProcessInfo> snapshot)
    {
        var claudePids = snapshot.Where(p => NamePattern.Matches("claude", p.Name)).Select(p => p.Pid).ToHashSet();
        return snapshot.Count(p => claudePids.Contains(p.Pid) && !claudePids.Contains(p.ParentPid));
    }
}
```

`src/RamCleaner.Core/Memory/WorkingSetTrimmer.cs`:

```csharp
using RamCleaner.Core.Classification;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Memory;

public sealed record TrimResult(int ProcessCount, long WorkingSetReducedBytes);

/// <summary>
/// Empties the working set of processes that used (almost) no CPU since the previous call.
/// Not thread-safe: call from a single timer.
/// </summary>
public sealed class WorkingSetTrimmer(IProcessControl processes, Func<int> foregroundPid)
{
    private static readonly TimeSpan IdleCpuThreshold = TimeSpan.FromMilliseconds(50);
    private static readonly string[] Excluded = ["vmmem*"];

    private Dictionary<int, (string Name, TimeSpan Cpu)> _previous = [];

    public TrimResult TrimIdle()
    {
        var foreground = foregroundPid();
        var current = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        var trimmed = new Dictionary<int, long>();

        foreach (var p in processes.Snapshot())
        {
            current[p.Pid] = (p.Name, p.CpuTime);
            if (p.Pid == foreground || p.Pid == processes.CurrentProcessId || p.IsCritical
                || ProtectedProcesses.IsProtected(p.Name) || NamePattern.MatchesAny(Excluded, p.Name))
                continue;
            if (!_previous.TryGetValue(p.Pid, out var prev) || !string.Equals(prev.Name, p.Name, StringComparison.Ordinal))
                continue;
            if (p.CpuTime - prev.Cpu > IdleCpuThreshold) continue;
            if (processes.TrimWorkingSet(p.Pid)) trimmed[p.Pid] = p.WorkingSetBytes;
        }

        _previous = current;
        if (trimmed.Count == 0) return new TrimResult(0, 0);

        long reduced = 0;
        foreach (var p in processes.Snapshot())
            if (trimmed.TryGetValue(p.Pid, out var before)) reduced += Math.Max(0, before - p.WorkingSetBytes);
        return new TrimResult(trimmed.Count, reduced);
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "FullyQualifiedName~MemoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): idle working-set trimmer, standby policy and Claude session counter"
```

---

### Task 8: Windows implementasyonları + GuardRunner

**Files:**
- Create: `src/RamCleaner.Core/Native/Win32.cs`, `src/RamCleaner.Core/Services/ScNative.cs`, `Services/WindowsServiceControl.cs`, `src/RamCleaner.Core/Processes/WindowsProcessControl.cs`, `src/RamCleaner.Core/Memory/SystemMemory.cs`, `src/RamCleaner.Core/Enforcement/ProcessStartWatcher.cs`, `Enforcement/GuardRunner.cs`
- Test: `tests/RamCleaner.Core.Tests/WindowsIntegrationTests.cs`

**Interfaces:**
- Consumes: `IServiceControl`, `ServiceState`, `ServiceStartType`, `IProcessControl`, `ProcessInfo`, `MemoryStatus`, `Guard`, `ActivityLog`, `LogKind`, `WorkingSetTrimmer`, `StandbyPolicy`, `UserConfig`
- Produces:
  - `sealed class WindowsServiceControl : IServiceControl`; `sealed class WindowsProcessControl : IProcessControl`
  - `static class SystemMemory { MemoryStatus Read(); int ForegroundProcessId(); bool PurgeStandbyList(); }` (namespace `RamCleaner.Core.Memory`)
  - `sealed class ProcessStartWatcher(Action<int> onStarted) : IDisposable { void Start(); void Stop(); }`
  - `sealed class GuardRunner(Guard guard, IProcessControl processes, ActivityLog log, Func<UserConfig> config) : IDisposable { void Start(); void Stop(); void PurgeStandby(); }` (namespace `RamCleaner.Core.Enforcement`)

> Bu görevdeki kod gerçek sisteme dokunur. Entegrasyon testleri yalnızca **okuma** ve test sırasında kendi başlattığı `ping` sürecini öldürme yapar; yönetici yetkisi gerektirmez. Servis değiştirme / WMI olayları manuel olarak Task 10'da doğrulanır.

- [ ] **Step 1: Failing entegrasyon testleri** — `tests/RamCleaner.Core.Tests/WindowsIntegrationTests.cs`

```csharp
using System.Diagnostics;
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Services;

namespace RamCleaner.Core.Tests;

[Trait("Category", "Windows")]
public class WindowsIntegrationTests
{
    private readonly WindowsProcessControl _processes = new();
    private readonly WindowsServiceControl _services = new();

    [Fact]
    public void Snapshot_ContainsCurrentProcess_WithSessionAndParent()
    {
        var self = Assert.Single(_processes.Snapshot(), p => p.Pid == Environment.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().SessionId, self.SessionId);
        Assert.NotEqual(0, self.ParentPid);
        Assert.True(self.WorkingSetBytes > 0);
        Assert.False(self.IsCritical);
    }

    [Fact]
    public void TryGet_UnknownPid_ReturnsNull() => Assert.Null(_processes.TryGet(int.MaxValue - 1));

    [Fact]
    public void Kill_StopsAProcessWeStarted()
    {
        using var ping = Process.Start(new ProcessStartInfo("ping", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(_processes.Kill(ping.Id));
        Assert.True(ping.WaitForExit(5000));
    }

    [Fact]
    public void TrimWorkingSet_OnOwnProcess_Succeeds() => Assert.True(_processes.TrimWorkingSet(Environment.ProcessId));

    [Fact]
    public void Query_KnownService_ReturnsState()
    {
        var state = _services.Query("EventLog");
        Assert.NotNull(state);
        Assert.True(state.IsRunning);
        Assert.Equal(ServiceStartType.Automatic, state.StartType);
        Assert.NotEqual(0, state.ProcessId);
    }

    [Fact]
    public void Query_MissingService_ReturnsNull() => Assert.Null(_services.Query("RamCleanerNoSuchService123"));

    [Fact]
    public void SystemMemory_ReadsSaneValues()
    {
        var m = SystemMemory.Read();
        Assert.True(m.TotalBytes > 1UL << 30);
        Assert.InRange(m.AvailableBytes, 1UL, m.TotalBytes);
    }
}
```

- [ ] **Step 2: Fail olduğunu doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "Category=Windows"`
Expected: Build FAILED — `WindowsProcessControl`, `WindowsServiceControl`, `SystemMemory` tanımlı değil.

- [ ] **Step 3: Native tanımlar**

`src/RamCleaner.Core/Native/Win32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace RamCleaner.Core.Native;

internal static class Win32
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const int SE_PRIVILEGE_ENABLED = 0x00000002;
    public const int SystemMemoryListInformation = 80;
    public const int MemoryPurgeStandbyList = 4;
    public static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsProcessCritical(IntPtr process, out bool critical);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "K32EmptyWorkingSet")]
    public static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("ntdll.dll")]
    public static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValueW(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>TOKEN_PRIVILEGES with exactly one LUID_AND_ATTRIBUTES (LUID is 4-byte aligned).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public long Luid;
        public int Attributes;
    }
}
```

`src/RamCleaner.Core/Services/ScNative.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RamCleaner.Core.Services;

internal static class ScNative
{
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
    public const int SC_STATUS_PROCESS_INFO = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_DELAYED_AUTO_START_INFO { public int fDelayedAutostart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ChangeServiceConfigW(IntPtr service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out SERVICE_STATUS_PROCESS buffer, int bufferSize, out int bytesNeeded);

    public static T WithService<T>(string name, uint access, Func<IntPtr, T> action)
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenServiceW(scm, name, access);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return action(service); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
```

- [ ] **Step 4: WindowsServiceControl** — `src/RamCleaner.Core/Services/WindowsServiceControl.cs`

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RamCleaner.Core.Services;

public sealed class WindowsServiceControl : IServiceControl
{
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public ServiceState? Query(string name)
    {
        using var sc = new ServiceController(name);
        try
        {
            var status = sc.Status;
            var running = status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending);
            return new ServiceState(sc.ServiceName, (ServiceStartType)(int)sc.StartType, ReadDelayedAutoStart(sc.ServiceName), running, ReadProcessId(sc.ServiceName));
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorServiceDoesNotExist })
        {
            return null;
        }
    }

    public void SetStartType(string name, ServiceStartType startType, bool delayedAutoStart) =>
        ScNative.WithService(name, ScNative.SERVICE_CHANGE_CONFIG, handle =>
        {
            if (!ScNative.ChangeServiceConfigW(handle, ScNative.SERVICE_NO_CHANGE, (uint)startType, ScNative.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (startType == ServiceStartType.Automatic)
            {
                var info = new ScNative.SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayedAutoStart ? 1 : 0 };
                if (!ScNative.ChangeServiceConfig2W(handle, ScNative.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return true;
        });

    public void Stop(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Stopped) return;
        if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop(stopDependentServices: false);
        sc.WaitForStatus(ServiceControllerStatus.Stopped, WaitTimeout);
    }

    public void Start(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
    }

    private static bool ReadDelayedAutoStart(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key?.GetValue("DelayedAutostart") is int value && value == 1;
    }

    private static int ReadProcessId(string name)
    {
        try
        {
            return ScNative.WithService(name, ScNative.SERVICE_QUERY_STATUS, handle =>
                ScNative.QueryServiceStatusEx(handle, ScNative.SC_STATUS_PROCESS_INFO, out var status,
                    Marshal.SizeOf<ScNative.SERVICE_STATUS_PROCESS>(), out _)
                    ? (int)status.dwProcessId
                    : 0);
        }
        catch (Win32Exception)
        {
            return 0;
        }
    }
}
```

- [ ] **Step 5: WindowsProcessControl** — `src/RamCleaner.Core/Processes/WindowsProcessControl.cs`

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RamCleaner.Core.Native;

namespace RamCleaner.Core.Processes;

public sealed class WindowsProcessControl : IProcessControl
{
    public int CurrentProcessId { get; } = Environment.ProcessId;

    public IReadOnlyList<ProcessInfo> Snapshot()
    {
        var parents = ReadParentMap();
        var result = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result.Add(Describe(process, parents)); }
                catch (InvalidOperationException) { /* exited during enumeration */ }
            }
        }
        return result;
    }

    public ProcessInfo? TryGet(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return Describe(process, ReadParentMap());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public bool Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: false);
            return process.WaitForExit(3000);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    public bool TrimWorkingSet(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_SET_QUOTA, false, pid);
        if (handle == IntPtr.Zero) return false;
        try { return Win32.EmptyWorkingSet(handle); }
        finally { Win32.CloseHandle(handle); }
    }

    private static ProcessInfo Describe(Process process, IReadOnlyDictionary<int, int> parents)
    {
        var (critical, cpu) = QueryHandleInfo(process.Id);
        return new ProcessInfo(process.Id, process.ProcessName, process.SessionId, process.WorkingSet64,
            parents.GetValueOrDefault(process.Id), critical, cpu);
    }

    /// <summary>Processes we cannot open (protected/PPL) are reported as critical so they are never touched.</summary>
    private static (bool Critical, TimeSpan Cpu) QueryHandleInfo(int pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return (true, TimeSpan.Zero);
        try
        {
            var critical = !Win32.IsProcessCritical(handle, out var isCritical) || isCritical;
            var cpu = Win32.GetProcessTimes(handle, out _, out _, out var kernel, out var user)
                ? TimeSpan.FromTicks(kernel + user)
                : TimeSpan.Zero;
            return (critical, cpu);
        }
        finally { Win32.CloseHandle(handle); }
    }

    private static Dictionary<int, int> ReadParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (snapshot == Win32.InvalidHandle) return map;
        try
        {
            var entry = new Win32.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<Win32.PROCESSENTRY32W>() };
            for (var ok = Win32.Process32FirstW(snapshot, ref entry); ok; ok = Win32.Process32NextW(snapshot, ref entry))
                map[entry.th32ProcessID] = entry.th32ParentProcessID;
        }
        finally { Win32.CloseHandle(snapshot); }
        return map;
    }
}
```

- [ ] **Step 6: SystemMemory** — `src/RamCleaner.Core/Memory/SystemMemory.cs`

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using RamCleaner.Core.Native;

namespace RamCleaner.Core.Memory;

public static class SystemMemory
{
    public static MemoryStatus Read()
    {
        var status = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (!Win32.GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new MemoryStatus(status.ullTotalPhys, status.ullAvailPhys);
    }

    public static int ForegroundProcessId()
    {
        var window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        Win32.GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    /// <summary>Empties the standby (file cache) list. Requires administrator rights.</summary>
    public static bool PurgeStandbyList()
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return false;
        var command = Win32.MemoryPurgeStandbyList;
        return Win32.NtSetSystemInformation(Win32.SystemMemoryListInformation, ref command, sizeof(int)) == 0;
    }

    private static bool EnablePrivilege(string privilege)
    {
        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TOKEN_ADJUST_PRIVILEGES | Win32.TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!Win32.LookupPrivilegeValueW(null, privilege, out var luid)) return false;
            var tp = new Win32.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = Win32.SE_PRIVILEGE_ENABLED };
            return Win32.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                && Marshal.GetLastWin32Error() == 0;
        }
        finally { Win32.CloseHandle(token); }
    }
}
```

- [ ] **Step 7: Entegrasyon testlerinin geçtiğini doğrula**

Run: `dotnet test tests/RamCleaner.Core.Tests --filter "Category=Windows"`
Expected: PASS (7 test).

- [ ] **Step 8: ProcessStartWatcher + GuardRunner**

`src/RamCleaner.Core/Enforcement/ProcessStartWatcher.cs`:

```csharp
using System.Management;

namespace RamCleaner.Core.Enforcement;

/// <summary>Near-instant process start notifications via WMI (requires administrator rights).</summary>
public sealed class ProcessStartWatcher : IDisposable
{
    private readonly ManagementEventWatcher _watcher;

    public ProcessStartWatcher(Action<int> onStarted)
    {
        _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += (_, e) => onStarted(Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value));
    }

    public void Start() => _watcher.Start();
    public void Stop() => _watcher.Stop();
    public void Dispose() => _watcher.Dispose();
}
```

`src/RamCleaner.Core/Enforcement/GuardRunner.cs`:

```csharp
using RamCleaner.Core.Config;
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;

namespace RamCleaner.Core.Enforcement;

/// <summary>Drives the Guard while active: WMI start events, 2 s process poll, 5 s service check, 10 s memory tick.</summary>
public sealed class GuardRunner : IDisposable
{
    private static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ServiceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MemoryInterval = TimeSpan.FromSeconds(10);

    private readonly Guard _guard;
    private readonly ActivityLog _log;
    private readonly Func<UserConfig> _config;
    private readonly WorkingSetTrimmer _trimmer;
    private readonly Timer _processTimer;
    private readonly Timer _serviceTimer;
    private readonly Timer _memoryTimer;
    private readonly object _gate = new();
    private ProcessStartWatcher? _watcher;
    private DateTime _lastTrim = DateTime.MinValue;
    private DateTime? _lastPurge;
    private int _memoryBusy;
    private bool _disposed;

    public GuardRunner(Guard guard, IProcessControl processes, ActivityLog log, Func<UserConfig> config)
    {
        _guard = guard;
        _log = log;
        _config = config;
        _trimmer = new WorkingSetTrimmer(processes, SystemMemory.ForegroundProcessId);
        _processTimer = new Timer(_ => Safe(_guard.EnforceProcesses));
        _serviceTimer = new Timer(_ => Safe(_guard.EnforceServices));
        _memoryTimer = new Timer(_ => Safe(MemoryTick));
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _processTimer.Change(ProcessInterval, ProcessInterval);
            _serviceTimer.Change(ServiceInterval, ServiceInterval);
            _memoryTimer.Change(MemoryInterval, MemoryInterval);
            try
            {
                _watcher = new ProcessStartWatcher(pid => Safe(() => _guard.OnProcessStarted(pid)));
                _watcher.Start();
            }
            catch (Exception ex)
            {
                _watcher?.Dispose();
                _watcher = null;
                _log.Write(LogKind.Warning, $"Process start events unavailable, using 2 s polling only: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _processTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _serviceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _memoryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_watcher != null)
            {
                Safe(_watcher.Stop);
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    public void PurgeStandby()
    {
        var ok = SystemMemory.PurgeStandbyList();
        _log.Write(ok ? LogKind.Info : LogKind.Warning,
            ok ? "Standby cache purged" : "Standby cache purge failed (administrator rights required)");
    }

    private void MemoryTick()
    {
        if (Interlocked.CompareExchange(ref _memoryBusy, 1, 0) != 0) return;
        try
        {
            var config = _config();
            var now = DateTime.Now;
            if (config.TrimEnabled && now - _lastTrim >= TimeSpan.FromSeconds(config.TrimIntervalSeconds))
            {
                _lastTrim = now;
                var result = _trimmer.TrimIdle();
                _guard.AddReclaimed(result.WorkingSetReducedBytes);
                if (result.ProcessCount > 0)
                    _log.Write(LogKind.Info, $"Trimmed {result.ProcessCount} idle process(es), working set -{result.WorkingSetReducedBytes / (1024 * 1024)} MB");
            }
            if (config.AutoStandbyPurge && StandbyPolicy.ShouldPurge(SystemMemory.Read(), config.StandbyPurgeThresholdPercent, _lastPurge, now))
            {
                _lastPurge = now;
                PurgeStandby();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _memoryBusy, 0);
        }
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _processTimer.Dispose();
            _serviceTimer.Dispose();
            _memoryTimer.Dispose();
        }
    }
}
```

- [ ] **Step 9: Build + tüm testler**

Run: `dotnet build RamCleaner.slnx && dotnet test tests/RamCleaner.Core.Tests`
Expected: Build succeeded, 0 warning; tüm testler PASS.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(core): Windows service/process/memory implementations and GuardRunner"
```

---

### Task 9: WPF uygulaması

**Files:**
- Create: `src/RamCleaner.App/RamCleaner.App.csproj`, `app.manifest`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `L.cs`, `Rows.cs`, `MainViewModel.cs`, `TrayIcon.cs`, `StartupTask.cs`
- Modify: `RamCleaner.slnx` (App projesini ekle)

**Interfaces:**
- Consumes: `Guard`, `GuardRunner`, `ActivityLog`, `LogEntry`, `LogKind`, `BlockedProcessEvent`, `WindowsServiceControl`, `WindowsProcessControl`, `IProcessControl`, `SessionJournal`, `ConfigStore`, `UserConfig`, `GuardProfile`, `DefaultRules`, `RuleSet`, `EffectiveRules`, `NamePattern`, `ProcessClassifier`, `Verdict`, `SystemMemory`, `ClaudeSessionCounter`
- Produces: `RamCleaner.exe` (WinExe, admin)

UI için otomatik test yok (ince katman; tüm mantık Core'da test edildi). Doğrulama: derleme + Step 9'daki manuel kontrol.

- [ ] **Step 1: Projeyi oluştur**

```bash
dotnet new wpf -n RamCleaner.App -o src/RamCleaner.App
dotnet sln RamCleaner.slnx add src/RamCleaner.App
```

`src/RamCleaner.App/RamCleaner.App.csproj` içeriğini tamamen değiştir:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>RamCleaner</AssemblyName>
    <RootNamespace>RamCleaner.App</RootNamespace>
    <Version>0.1.0</Version>
    <!-- MVVM toolkit source generators can emit advisory warnings; keep the UI shell lenient. -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <!-- WPF + WinForms (tray icon) together: avoid ambiguous Application/MessageBox/etc. -->
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\RamCleaner.Core\RamCleaner.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/RamCleaner.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="RamCleaner.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Yerelleştirme** — `src/RamCleaner.App/L.cs`

```csharp
using System.Globalization;

namespace RamCleaner.App;

/// <summary>UI strings: English by default, Turkish when the OS UI language is Turkish.</summary>
public static class L
{
    private static readonly bool Turkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
    private static string T(string en, string tr) => Turkish ? tr : en;

    public static string Active => T("ACTIVE", "AKTİF");
    public static string Inactive => T("INACTIVE", "PASİF");
    public static string ToggleHint => T("Stop background bloat and keep it stopped while active", "Aktifken arka plan yüklerini durdurur ve durdurulmuş tutar");
    public static string Profile => T("Profile", "Profil");
    public static string Balanced => T("Balanced — block known bloat", "Dengeli — bilinen gereksizleri engelle");
    public static string Aggressive => T("Aggressive — only dev tools may run", "Agresif — sadece dev araçları çalışabilir");
    public static string Preview => T("Preview", "Önizle");
    public static string PurgeStandby => T("Purge standby cache", "Standby önbelleğini temizle");
    public static string TabDashboard => T("Dashboard", "Panel");
    public static string TabRules => T("Rules", "Kurallar");
    public static string TabSuggestions => T("Suggestions", "Öneriler");
    public static string TabSettings => T("Settings", "Ayarlar");
    public static string TotalRam => T("Total RAM", "Toplam RAM");
    public static string AvailableRam => T("Available", "Kullanılabilir");
    public static string ClaudeSessions => T("Claude Code sessions", "Claude Code oturumu");
    public static string Reclaimed => T("Reclaimed", "Kazanılan");
    public static string ActivityLog => T("Activity", "Etkinlik");
    public static string PreviewTitle => T("Will be stopped", "Durdurulacaklar");
    public static string BlockedServices => T("Blocked services (defaults)", "Engellenen servisler (varsayılan)");
    public static string BlockedProcesses => T("Blocked apps (defaults)", "Engellenen uygulamalar (varsayılan)");
    public static string DevTools => T("Dev tools (always allowed)", "Dev araçları (her zaman izinli)");
    public static string UserBlockedServices => T("Your blocked services", "Senin engellediğin servisler");
    public static string UserBlockedProcesses => T("Your blocked apps", "Senin engellediğin uygulamalar");
    public static string UserAllowed => T("Your always-allowed apps", "Senin izin verdiğin uygulamalar");
    public static string PatternHint => T("Process or service name; * wildcard allowed (e.g. lghub*)", "Süreç veya servis adı; * kullanılabilir (ör. lghub*)");
    public static string BlockApp => T("Block app", "Uygulamayı engelle");
    public static string AllowApp => T("Always allow app", "Uygulamaya hep izin ver");
    public static string BlockService => T("Block service", "Servisi engelle");
    public static string Remove => T("Remove", "Kaldır");
    public static string Block => T("Block", "Engelle");
    public static string Allow => T("Allow", "İzin ver");
    public static string Refresh => T("Refresh", "Yenile");
    public static string TrimEnabled => T("Trim working sets of idle processes", "Boştaki süreçlerin working set'ini küçült");
    public static string TrimInterval => T("Trim interval (seconds, 10–3600)", "Küçültme aralığı (saniye, 10–3600)");
    public static string AutoPurgeEnabled => T("Auto-purge standby cache when RAM is low", "RAM azalınca standby önbelleğini otomatik temizle");
    public static string AutoPurgeThreshold => T("Purge when available RAM is below (%)", "Kullanılabilir RAM şu yüzdenin altındaysa temizle (%)");
    public static string StartWithWindows => T("Start with Windows (minimized)", "Windows ile başlat (simge durumunda)");
    public static string Save => T("Save", "Kaydet");
    public static string Saved => T("Settings saved", "Ayarlar kaydedildi");
    public static string Open => T("Open", "Aç");
    public static string ToggleMenu => T("Active / Inactive", "Aktif / Pasif");
    public static string Exit => T("Exit", "Çıkış");
    public static string ColName => T("Name", "Ad");
    public static string ColKind => T("Type", "Tür");
    public static string ColMemory => T("Memory", "Bellek");
    public static string ColCount => T("Count", "Adet");
    public static string KindProcess => T("App", "Uygulama");
    public static string KindService => T("Service", "Servis");
    public static string Running => T("running", "çalışıyor");
    public static string ActivationFailed => T("Activation failed", "Aktivasyon başarısız");
    public static string EstimatedGain(string mb) => T($"Estimated gain: {mb}", $"Tahmini kazanç: {mb}");
    public static string BlockedToast(string name) => T($"{name} was blocked. Click to always allow it.", $"{name} engellendi. Her zaman izin vermek için tıkla.");
    public static string RecoveredToast(int count) => T($"Restored {count} service(s) left disabled by a previous session.", $"Önceki oturumdan kalan {count} servis geri yüklendi.");
    public static string UnsafePattern(string pattern) => T($"'{pattern}' is too broad to block", $"'{pattern}' engellemek için fazla geniş");
}
```

- [ ] **Step 3: Satır modelleri** — `src/RamCleaner.App/Rows.cs`

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using RamCleaner.Core.Config;

namespace RamCleaner.App;

public sealed record ProfileOption(GuardProfile Value, string Label);

public sealed record PreviewRow(string Name, string Kind, string Memory);

public sealed record SuggestionRow(string Name, int Count, string Memory);

public sealed partial class RuleRow(string key, string name, string description, bool isEnabled, Action<RuleRow> onChanged) : ObservableObject
{
    public string Key => key;
    public string Name => name;
    public string Description => description;

    [ObservableProperty]
    private bool _isEnabled = isEnabled;

    partial void OnIsEnabledChanged(bool value) => onChanged(this);
}
```

- [ ] **Step 4: StartupTask** — `src/RamCleaner.App/StartupTask.cs`

```csharp
using System.Diagnostics;

namespace RamCleaner.App;

/// <summary>
/// "Start with Windows" via Task Scheduler with highest privileges: a Run-key entry would
/// show a UAC prompt on every logon because the app requires administrator rights.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "RamCleaner";

    public static void Apply(bool enabled)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown.");
        var arguments = enabled
            ? $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{exe}\\\" --minimized\""
            : $"/Delete /F /TN \"{TaskName}\"";

        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        if (enabled && process.ExitCode != 0) throw new InvalidOperationException($"schtasks failed: {error.Trim()}");
    }
}
```

- [ ] **Step 5: ViewModel** — `src/RamCleaner.App/MainViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RamCleaner.Core.Classification;
using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.App;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(60);

    private readonly Guard _guard;
    private readonly IProcessControl _processes;
    private readonly ConfigStore _store;
    private readonly ActivityLog _log;
    private readonly Dispatcher _ui;
    private readonly GuardRunner _runner;
    private readonly DispatcherTimer _metricsTimer;
    private readonly RuleSet _defaults = DefaultRules.Load();
    private readonly Dictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private UserConfig _config;
    private bool _refreshingMetrics;
    private bool _shutDown;

    public MainViewModel(Guard guard, IProcessControl processes, ConfigStore store, ActivityLog log, Dispatcher ui)
    {
        _guard = guard;
        _processes = processes;
        _store = store;
        _log = log;
        _ui = ui;
        _config = store.Load();
        _runner = new GuardRunner(guard, processes, log, () => _config);

        Profiles = [new(GuardProfile.Balanced, L.Balanced), new(GuardProfile.Aggressive, L.Aggressive)];
        SelectedProfile = Profiles.First(p => p.Value == _config.Profile);
        LoadRuleRows();
        SyncUserLists();
        LoadSettings();

        foreach (var entry in log.Snapshot().Reverse()) Log.Add(entry);
        log.Added += entry => _ui.InvokeAsync(() =>
        {
            Log.Insert(0, entry);
            if (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
        });
        guard.ProcessBlocked += OnProcessBlocked;

        _metricsTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshMetricsAsync(), ui);
        _ = RefreshMetricsAsync();
    }

    /// <summary>Raised on the UI thread, at most once per name per minute.</summary>
    public event Action<string>? BlockedNotification;

    public IReadOnlyList<ProfileOption> Profiles { get; }
    public IReadOnlyList<string> DevTools => _defaults.DevAllowedProcesses;
    public ObservableCollection<LogEntry> Log { get; } = [];
    public ObservableCollection<PreviewRow> PreviewRows { get; } = [];
    public ObservableCollection<RuleRow> ServiceRules { get; } = [];
    public ObservableCollection<RuleRow> ProcessRules { get; } = [];
    public ObservableCollection<string> UserAllowed { get; } = [];
    public ObservableCollection<string> UserBlocked { get; } = [];
    public ObservableCollection<string> UserBlockedServices { get; } = [];
    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];

    [ObservableProperty] private bool _isActive;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ToggleCommand))] private bool _isBusy;
    [ObservableProperty] private ProfileOption? _selectedProfile;
    [ObservableProperty] private string _totalRam = "";
    [ObservableProperty] private string _availableRam = "";
    [ObservableProperty] private double _usedPercent;
    [ObservableProperty] private int _claudeSessions;
    [ObservableProperty] private string _reclaimed = "0 MB";
    [ObservableProperty] private string _previewSummary = "";
    [ObservableProperty] private string _newPattern = "";
    [ObservableProperty] private bool _trimEnabled;
    [ObservableProperty] private int _trimIntervalSeconds;
    [ObservableProperty] private bool _autoStandbyPurge;
    [ObservableProperty] private int _standbyPurgeThresholdPercent;
    [ObservableProperty] private bool _startWithWindows;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            if (_guard.IsActive)
            {
                _runner.Stop();
                await Task.Run(_guard.Deactivate);
            }
            else
            {
                var rules = BuildRules();
                await Task.Run(() => _guard.Activate(rules));
                _runner.Start();
            }
        }
        catch (Exception ex)
        {
            _log.Write(LogKind.Warning, $"{L.ActivationFailed}: {ex.Message}");
        }
        finally
        {
            IsActive = _guard.IsActive;
            IsBusy = false;
        }
    }

    private bool CanToggle() => !IsBusy;

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var rules = BuildRules();
        var result = await Task.Run(() => _guard.Preview(rules));
        PreviewRows.Clear();
        foreach (var group in result.Processes
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(p => p.WorkingSetBytes)))
        {
            var label = group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key;
            PreviewRows.Add(new PreviewRow(label, L.KindProcess, Mb(group.Sum(p => p.WorkingSetBytes))));
        }
        foreach (var service in result.Services)
            PreviewRows.Add(new PreviewRow(service.Name, L.KindService, service.IsRunning ? L.Running : "—"));
        PreviewSummary = L.EstimatedGain(Mb(result.EstimatedBytes));
    }

    [RelayCommand]
    private Task PurgeStandbyAsync() => Task.Run(_runner.PurgeStandby);

    [RelayCommand]
    private void BlockAppFromInput()
    {
        BlockProcess(NewPattern);
        NewPattern = "";
    }

    [RelayCommand]
    private void AllowAppFromInput()
    {
        AllowProcess(NewPattern);
        NewPattern = "";
    }

    [RelayCommand]
    private void BlockServiceFromInput()
    {
        var name = NewPattern.Trim();
        NewPattern = "";
        if (name.Length == 0 || _config.BlockedServices.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        _config.BlockedServices.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    [RelayCommand]
    private void RemoveUserBlocked(string name) => RemoveFrom(_config.BlockedProcesses, name);

    [RelayCommand]
    private void RemoveUserAllowed(string name) => RemoveFrom(_config.AllowedProcesses, name);

    [RelayCommand]
    private void RemoveUserBlockedService(string name) => RemoveFrom(_config.BlockedServices, name);

    [RelayCommand]
    private void BlockSuggestion(SuggestionRow row)
    {
        BlockProcess(row.Name);
        Suggestions.Remove(row);
    }

    [RelayCommand]
    private void AllowSuggestion(SuggestionRow row)
    {
        AllowProcess(row.Name);
        Suggestions.Remove(row);
    }

    [RelayCommand]
    private async Task RefreshSuggestionsAsync()
    {
        var classifier = new ProcessClassifier(BuildRules(), new HashSet<int>());
        var snapshot = await Task.Run(() => _processes.Snapshot());
        Suggestions.Clear();
        foreach (var group in snapshot
                     .Where(p => p.SessionId != 0 && classifier.Classify(p) == Verdict.Unknown)
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(p => p.WorkingSetBytes)))
        {
            Suggestions.Add(new SuggestionRow(group.Key, group.Count(), Mb(group.Sum(p => p.WorkingSetBytes))));
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _config.TrimEnabled = TrimEnabled;
        _config.TrimIntervalSeconds = TrimIntervalSeconds;
        _config.AutoStandbyPurge = AutoStandbyPurge;
        _config.StandbyPurgeThresholdPercent = StandbyPurgeThresholdPercent;
        if (StartWithWindows != _config.StartWithWindows)
        {
            try
            {
                StartupTask.Apply(StartWithWindows);
                _config.StartWithWindows = StartWithWindows;
            }
            catch (Exception ex)
            {
                _log.Write(LogKind.Warning, ex.Message);
            }
        }
        _store.Save(_config);
        _config = _store.Load(); // re-applies clamping
        LoadSettings();
        _log.Write(LogKind.Info, L.Saved);
    }

    public void AllowProcess(string name)
    {
        name = NamePattern.Normalize(name);
        if (name.Length == 0) return;
        _config.BlockedProcesses.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (!_config.AllowedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase)) _config.AllowedProcesses.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    /// <summary>Stops the guard and restores services. Safe to call more than once.</summary>
    public void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _metricsTimer.Stop();
        _runner.Dispose();
        try { _guard.Deactivate(); }
        catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        if (value is null || value.Value == _config.Profile) return;
        _config.Profile = value.Value;
        SaveAndApply();
    }

    private void BlockProcess(string name)
    {
        name = NamePattern.Normalize(name);
        if (!NamePattern.IsSafeBlockPattern(name))
        {
            _log.Write(LogKind.Warning, L.UnsafePattern(name));
            return;
        }
        _config.AllowedProcesses.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (!_config.BlockedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase)) _config.BlockedProcesses.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    private void RemoveFrom(List<string> list, string name)
    {
        list.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        SyncUserLists();
        SaveAndApply();
    }

    private EffectiveRules BuildRules() => EffectiveRules.Build(_defaults, _config);

    private void SaveAndApply()
    {
        _store.Save(_config);
        if (!_guard.IsActive) return;
        var rules = BuildRules();
        _ = Task.Run(() =>
        {
            try { _guard.UpdateRules(rules); }
            catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
        });
    }

    private void OnProcessBlocked(BlockedProcessEvent e) => _ui.InvokeAsync(() =>
    {
        var now = DateTime.Now;
        if (_lastNotified.TryGetValue(e.Name, out var last) && now - last < NotifyCooldown) return;
        _lastNotified[e.Name] = now;
        BlockedNotification?.Invoke(e.Name);
    });

    private void LoadRuleRows()
    {
        foreach (var rule in _defaults.BlockedServices)
        {
            var key = UserConfig.ServiceKey(rule.Name);
            ServiceRules.Add(new RuleRow(key, rule.Name, rule.Description, IsRuleEnabled(key, rule.EnabledByDefault), OnRuleToggled));
        }
        foreach (var rule in _defaults.BlockedProcesses)
        {
            var key = UserConfig.ProcessKey(rule.Name);
            ProcessRules.Add(new RuleRow(key, rule.Name, rule.Description, IsRuleEnabled(key, rule.EnabledByDefault), OnRuleToggled));
        }
    }

    private bool IsRuleEnabled(string key, bool byDefault) =>
        _config.RuleToggles.TryGetValue(key, out var value) ? value : byDefault;

    private void OnRuleToggled(RuleRow row)
    {
        _config.RuleToggles[row.Key] = row.IsEnabled;
        SaveAndApply();
    }

    private void SyncUserLists()
    {
        Replace(UserAllowed, _config.AllowedProcesses);
        Replace(UserBlocked, _config.BlockedProcesses);
        Replace(UserBlockedServices, _config.BlockedServices);
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void LoadSettings()
    {
        TrimEnabled = _config.TrimEnabled;
        TrimIntervalSeconds = _config.TrimIntervalSeconds;
        AutoStandbyPurge = _config.AutoStandbyPurge;
        StandbyPurgeThresholdPercent = _config.StandbyPurgeThresholdPercent;
        StartWithWindows = _config.StartWithWindows;
    }

    private async Task RefreshMetricsAsync()
    {
        if (_refreshingMetrics) return;
        _refreshingMetrics = true;
        try
        {
            var memory = SystemMemory.Read();
            TotalRam = Gb(memory.TotalBytes);
            AvailableRam = $"{Gb(memory.AvailableBytes)} ({memory.AvailablePercent:0}%)";
            UsedPercent = memory.UsedPercent;
            Reclaimed = Mb(_guard.ReclaimedBytes);
            var snapshot = await Task.Run(() => _processes.Snapshot());
            ClaudeSessions = ClaudeSessionCounter.Count(snapshot);
        }
        catch (Exception ex)
        {
            _log.Write(LogKind.Warning, ex.Message);
        }
        finally
        {
            _refreshingMetrics = false;
        }
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:N0} MB";
    private static string Gb(ulong bytes) => $"{bytes / 1073741824.0:0.0} GB";
}
```

- [ ] **Step 6: Tray ikonu** — `src/RamCleaner.App/TrayIcon.cs`

```csharp
using System.Windows;
using Forms = System.Windows.Forms;

namespace RamCleaner.App;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly MainWindow _window;
    private string? _pendingAllow;

    public TrayIcon(MainWindow window, MainViewModel vm, Action exit)
    {
        _window = window;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L.Open, null, (_, _) => ShowWindow());
        menu.Items.Add(L.ToggleMenu, null, (_, _) => vm.ToggleCommand.Execute(null));
        menu.Items.Add(L.PurgeStandby, null, (_, _) => vm.PurgeStandbyCommand.Execute(null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.Exit, null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = $"RamCleaner — {L.Inactive}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_pendingAllow is { } name) vm.AllowProcess(name);
            _pendingAllow = null;
        };
        vm.BlockedNotification += name =>
        {
            _pendingAllow = name;
            _icon.ShowBalloonTip(4000, "RamCleaner", L.BlockedToast(name), Forms.ToolTipIcon.Info);
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsActive))
                _icon.Text = $"RamCleaner — {(vm.IsActive ? L.Active : L.Inactive)}";
        };
    }

    public void ShowInfo(string text)
    {
        _pendingAllow = null;
        _icon.ShowBalloonTip(4000, "RamCleaner", text, Forms.ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

- [ ] **Step 7: Uygulama giriş noktası**

`src/RamCleaner.App/App.xaml` (template dosyasını tamamen değiştir — `StartupUri` yok):

```xml
<Application x:Class="RamCleaner.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources />
</Application>
```

`src/RamCleaner.App/App.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Services;
using RamCleaner.Core.State;

namespace RamCleaner.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Global\RamCleaner.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);

        var log = new ActivityLog();
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RamCleaner");
        var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RamCleaner", "config.json");
        var processes = new WindowsProcessControl();
        var guard = new Guard(new WindowsServiceControl(), processes, new SessionJournal(Path.Combine(dataDir, "session.json")), log);

        DispatcherUnhandledException += (_, args) =>
        {
            log.Write(LogKind.Warning, args.Exception.Message);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _vm?.Shutdown();
        SessionEnding += (_, _) => _vm?.Shutdown();

        var recovered = guard.RecoverIfNeeded();

        _vm = new MainViewModel(guard, processes, new ConfigStore(configPath), log, Dispatcher);
        _window = new MainWindow { DataContext = _vm };
        _tray = new TrayIcon(_window, _vm, ExitApp);
        if (recovered > 0) _tray.ShowInfo(L.RecoveredToast(recovered));
        if (!e.Args.Contains("--minimized")) _window.Show();
    }

    public void ExitApp()
    {
        if (_window != null) _window.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Shutdown();
        _tray?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 8: Ana pencere**

`src/RamCleaner.App/MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;

namespace RamCleaner.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Set by App.ExitApp; otherwise closing the window only hides it to the tray.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
```

`src/RamCleaner.App/MainWindow.xaml`:

```xml
<Window x:Class="RamCleaner.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:app="clr-namespace:RamCleaner.App"
        Title="RamCleaner" Width="980" Height="680" MinWidth="760" MinHeight="540"
        WindowStartupLocation="CenterScreen">
    <Window.Resources>
        <Style x:Key="Caption" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#666" />
        </Style>
        <Style x:Key="Metric" TargetType="TextBlock">
            <Setter Property="FontSize" Value="22" />
            <Setter Property="FontWeight" Value="SemiBold" />
        </Style>
    </Window.Resources>

    <DockPanel Margin="16">
        <!-- Header: big toggle + profile + actions -->
        <Grid DockPanel.Dock="Top" Margin="0,0,0,12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Button Command="{Binding ToggleCommand}" Width="190" Height="76" FontSize="24" FontWeight="Bold"
                    ToolTip="{x:Static app:L.ToggleHint}">
                <Button.Style>
                    <Style TargetType="Button">
                        <Setter Property="Content" Value="{x:Static app:L.Inactive}" />
                        <Setter Property="Background" Value="#9E9E9E" />
                        <Setter Property="Foreground" Value="White" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsActive}" Value="True">
                                <Setter Property="Content" Value="{x:Static app:L.Active}" />
                                <Setter Property="Background" Value="#2E7D32" />
                            </DataTrigger>
                            <DataTrigger Binding="{Binding IsBusy}" Value="True">
                                <Setter Property="Content" Value="…" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Button.Style>
            </Button>
            <StackPanel Grid.Column="1" Margin="16,0,0,0" VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{x:Static app:L.Profile}" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <ComboBox ItemsSource="{Binding Profiles}" SelectedItem="{Binding SelectedProfile}"
                              DisplayMemberPath="Label" Width="320" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <Button Content="{x:Static app:L.Preview}" Command="{Binding PreviewCommand}" Padding="12,4" Margin="0,0,8,0" />
                    <Button Content="{x:Static app:L.PurgeStandby}" Command="{Binding PurgeStandbyCommand}" Padding="12,4" />
                </StackPanel>
            </StackPanel>
        </Grid>

        <TabControl>
            <!-- Dashboard -->
            <TabItem Header="{x:Static app:L.TabDashboard}">
                <Grid Margin="8">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <UniformGrid Grid.ColumnSpan="2" Columns="4">
                        <StackPanel>
                            <TextBlock Style="{StaticResource Caption}" Text="{x:Static app:L.TotalRam}" />
                            <TextBlock Style="{StaticResource Metric}" Text="{Binding TotalRam}" />
                        </StackPanel>
                        <StackPanel>
                            <TextBlock Style="{StaticResource Caption}" Text="{x:Static app:L.AvailableRam}" />
                            <TextBlock Style="{StaticResource Metric}" Text="{Binding AvailableRam}" />
                        </StackPanel>
                        <StackPanel>
                            <TextBlock Style="{StaticResource Caption}" Text="{x:Static app:L.ClaudeSessions}" />
                            <TextBlock Style="{StaticResource Metric}" Text="{Binding ClaudeSessions}" />
                        </StackPanel>
                        <StackPanel>
                            <TextBlock Style="{StaticResource Caption}" Text="{x:Static app:L.Reclaimed}" />
                            <TextBlock Style="{StaticResource Metric}" Text="{Binding Reclaimed}" />
                        </StackPanel>
                    </UniformGrid>
                    <ProgressBar Grid.Row="1" Grid.ColumnSpan="2" Height="12" Margin="0,10" Maximum="100"
                                 Value="{Binding UsedPercent, Mode=OneWay}" />
                    <GroupBox Grid.Row="2" Header="{x:Static app:L.ActivityLog}" Margin="0,0,6,0">
                        <ListBox ItemsSource="{Binding Log}" BorderThickness="0">
                            <ListBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock TextWrapping="Wrap">
                                        <Run Text="{Binding Time, StringFormat=HH:mm:ss, Mode=OneWay}" Foreground="#888" />
                                        <Run Text="{Binding Kind, Mode=OneWay}" FontWeight="SemiBold" />
                                        <Run Text="{Binding Message, Mode=OneWay}" />
                                    </TextBlock>
                                </DataTemplate>
                            </ListBox.ItemTemplate>
                        </ListBox>
                    </GroupBox>
                    <GroupBox Grid.Row="2" Grid.Column="1" Header="{x:Static app:L.PreviewTitle}" Margin="6,0,0,0">
                        <DockPanel>
                            <TextBlock DockPanel.Dock="Top" Text="{Binding PreviewSummary}" FontWeight="SemiBold" Margin="4" />
                            <ListView ItemsSource="{Binding PreviewRows}">
                                <ListView.View>
                                    <GridView>
                                        <GridViewColumn Header="{x:Static app:L.ColName}" DisplayMemberBinding="{Binding Name}" Width="200" />
                                        <GridViewColumn Header="{x:Static app:L.ColKind}" DisplayMemberBinding="{Binding Kind}" Width="90" />
                                        <GridViewColumn Header="{x:Static app:L.ColMemory}" DisplayMemberBinding="{Binding Memory}" Width="90" />
                                    </GridView>
                                </ListView.View>
                            </ListView>
                        </DockPanel>
                    </GroupBox>
                </Grid>
            </TabItem>

            <!-- Rules -->
            <TabItem Header="{x:Static app:L.TabRules}">
                <DockPanel Margin="8">
                    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
                        <TextBox Text="{Binding NewPattern, UpdateSourceTrigger=PropertyChanged}" Width="260"
                                 ToolTip="{x:Static app:L.PatternHint}" VerticalContentAlignment="Center" />
                        <Button Content="{x:Static app:L.BlockApp}" Command="{Binding BlockAppFromInputCommand}" Margin="8,0,0,0" Padding="10,3" />
                        <Button Content="{x:Static app:L.AllowApp}" Command="{Binding AllowAppFromInputCommand}" Margin="8,0,0,0" Padding="10,3" />
                        <Button Content="{x:Static app:L.BlockService}" Command="{Binding BlockServiceFromInputCommand}" Margin="8,0,0,0" Padding="10,3" />
                    </StackPanel>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="2*" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>

                        <GroupBox Header="{x:Static app:L.BlockedServices}" Margin="0,0,6,6">
                            <ListBox ItemsSource="{Binding ServiceRules}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <CheckBox IsChecked="{Binding IsEnabled}" Content="{Binding Name}" ToolTip="{Binding Description}" />
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
                        </GroupBox>
                        <GroupBox Grid.Column="1" Header="{x:Static app:L.BlockedProcesses}" Margin="6,0,6,6">
                            <ListBox ItemsSource="{Binding ProcessRules}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <CheckBox IsChecked="{Binding IsEnabled}" Content="{Binding Name}" ToolTip="{Binding Description}" />
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
                        </GroupBox>
                        <GroupBox Grid.Column="2" Header="{x:Static app:L.DevTools}" Margin="6,0,0,6">
                            <ListBox ItemsSource="{Binding DevTools}" />
                        </GroupBox>

                        <GroupBox Grid.Row="1" Header="{x:Static app:L.UserBlockedServices}" Margin="0,0,6,0">
                            <ListBox ItemsSource="{Binding UserBlockedServices}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <DockPanel>
                                            <Button DockPanel.Dock="Right" Content="{x:Static app:L.Remove}" Padding="6,0"
                                                    Command="{Binding DataContext.RemoveUserBlockedServiceCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}" />
                                            <TextBlock Text="{Binding}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                        </DockPanel>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
                        </GroupBox>
                        <GroupBox Grid.Row="1" Grid.Column="1" Header="{x:Static app:L.UserBlockedProcesses}" Margin="6,0,6,0">
                            <ListBox ItemsSource="{Binding UserBlocked}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <DockPanel>
                                            <Button DockPanel.Dock="Right" Content="{x:Static app:L.Remove}" Padding="6,0"
                                                    Command="{Binding DataContext.RemoveUserBlockedCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}" />
                                            <TextBlock Text="{Binding}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                        </DockPanel>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
                        </GroupBox>
                        <GroupBox Grid.Row="1" Grid.Column="2" Header="{x:Static app:L.UserAllowed}" Margin="6,0,0,0">
                            <ListBox ItemsSource="{Binding UserAllowed}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <DockPanel>
                                            <Button DockPanel.Dock="Right" Content="{x:Static app:L.Remove}" Padding="6,0"
                                                    Command="{Binding DataContext.RemoveUserAllowedCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}" />
                                            <TextBlock Text="{Binding}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                        </DockPanel>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
                        </GroupBox>
                    </Grid>
                </DockPanel>
            </TabItem>

            <!-- Suggestions -->
            <TabItem Header="{x:Static app:L.TabSuggestions}">
                <DockPanel Margin="8">
                    <Button DockPanel.Dock="Top" HorizontalAlignment="Left" Content="{x:Static app:L.Refresh}"
                            Command="{Binding RefreshSuggestionsCommand}" Padding="10,3" Margin="0,0,0,8" />
                    <ListView ItemsSource="{Binding Suggestions}">
                        <ListView.View>
                            <GridView>
                                <GridViewColumn Header="{x:Static app:L.ColName}" DisplayMemberBinding="{Binding Name}" Width="260" />
                                <GridViewColumn Header="{x:Static app:L.ColCount}" DisplayMemberBinding="{Binding Count}" Width="70" />
                                <GridViewColumn Header="{x:Static app:L.ColMemory}" DisplayMemberBinding="{Binding Memory}" Width="100" />
                                <GridViewColumn Width="200">
                                    <GridViewColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal">
                                                <Button Content="{x:Static app:L.Block}" Padding="8,0" Margin="0,0,6,0"
                                                        Command="{Binding DataContext.BlockSuggestionCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}" />
                                                <Button Content="{x:Static app:L.Allow}" Padding="8,0"
                                                        Command="{Binding DataContext.AllowSuggestionCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </GridViewColumn.CellTemplate>
                                </GridViewColumn>
                            </GridView>
                        </ListView.View>
                    </ListView>
                </DockPanel>
            </TabItem>

            <!-- Settings -->
            <TabItem Header="{x:Static app:L.TabSettings}">
                <StackPanel Margin="12" MaxWidth="600" HorizontalAlignment="Left">
                    <CheckBox IsChecked="{Binding TrimEnabled}" Content="{x:Static app:L.TrimEnabled}" Margin="0,0,0,8" />
                    <DockPanel Margin="0,0,0,12">
                        <TextBox DockPanel.Dock="Right" Width="80" Text="{Binding TrimIntervalSeconds}" />
                        <TextBlock Text="{x:Static app:L.TrimInterval}" VerticalAlignment="Center" Margin="0,0,12,0" />
                    </DockPanel>
                    <CheckBox IsChecked="{Binding AutoStandbyPurge}" Content="{x:Static app:L.AutoPurgeEnabled}" Margin="0,0,0,8" />
                    <DockPanel Margin="0,0,0,12">
                        <TextBox DockPanel.Dock="Right" Width="80" Text="{Binding StandbyPurgeThresholdPercent}" />
                        <TextBlock Text="{x:Static app:L.AutoPurgeThreshold}" VerticalAlignment="Center" Margin="0,0,12,0" />
                    </DockPanel>
                    <CheckBox IsChecked="{Binding StartWithWindows}" Content="{x:Static app:L.StartWithWindows}" Margin="0,0,0,16" />
                    <Button Content="{x:Static app:L.Save}" Command="{Binding SaveSettingsCommand}" HorizontalAlignment="Left" Padding="16,4" />
                </StackPanel>
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```

- [ ] **Step 9: Derle ve manuel duman testi**

Run: `dotnet build RamCleaner.slnx`
Expected: Build succeeded (Core/Tests'te 0 warning).

Manuel (kullanıcı, UAC onaylayarak): `src/RamCleaner.App/bin/Debug/net10.0-windows/RamCleaner.exe` çalıştır →
1. Pencere açılır, Panel'de Toplam/Kullanılabilir RAM ve Claude Code oturum sayısı görünür.
2. **Önizle** → sağ panelde Spotify vb. ve servisler listelenir, "Tahmini kazanç" yazar; hiçbir şey durmamıştır.
3. Pencereyi kapat → tray'e iner; tray çift tık → geri gelir; tray **Çıkış** → uygulama kapanır.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(app): WPF shell with toggle, rules, suggestions, settings and tray"
```

---

### Task 10: Açık kaynak paketleme, CI ve uçtan uca doğrulama

**Files:**
- Create: `LICENSE`, `README.md`, `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: tüm önceki görevler

- [ ] **Step 1: LICENSE** (MIT)

```text
MIT License

Copyright (c) 2026 Oktay Parlak

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: README.md**

````markdown
# RamCleaner

One button that frees RAM for development on Windows. While **active** it stops background bloat
(telemetry services, game launchers, OEM helpers, chat/music apps…) and keeps it from coming back —
without touching your dev tools (Claude Code, Node, Python, Java, Docker/WSL, Android emulator,
Chrome/Edge, VS Code, JetBrains IDEs, terminals). Turn it off and every service is restored.

> 🇹🇷 Türkçe açıklama aşağıda.

## Features
- **Balanced** profile: blocks a curated list of known bloat. **Aggressive** profile: only dev tools and
  Windows itself may run in your session; anything else is closed (click the notification to allow it).
- Services are disabled while active and restored on deactivate, exit, logoff — and after a crash
  (write-ahead journal in `%ProgramData%\RamCleaner\session.json`).
- Idle working-set trimming, standby-cache purge, live RAM dashboard with Claude Code session count.
- Editable rules, suggestions from currently running apps, start with Windows.

## Install
Download `RamCleaner-win-x64.exe` (needs the .NET 10 Desktop Runtime) or
`RamCleaner-win-x64-selfcontained.exe` from Releases. The app asks for administrator rights
(needed to stop services and other processes).

## Safety
Windows core processes, security software, critical processes, processes it cannot inspect, and
RamCleaner's own parent chain are never touched. Services are only changed if they are on the
block list; the original start type is recorded *before* any change. Use **Preview** to see exactly
what will be stopped.

## Build
```bash
dotnet test RamCleaner.slnx
dotnet publish src/RamCleaner.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

## License
MIT

---

## Türkçe

Windows'ta geliştirme için RAM açan tek butonlu araç. **Aktifken** gereksiz arka plan servis ve
uygulamalarını durdurur ve geri gelmelerini engeller; geliştirme araçlarına (Claude Code, Node,
Python, Java, Docker/WSL, Android emülatörü, Chrome/Edge, VS Code, JetBrains, terminaller) dokunmaz.
Kapatınca tüm servisler eski haline döner — uygulama çökse bile bir sonraki açılışta geri yüklenir.

- **Dengeli**: bilinen gereksizleri engeller. **Agresif**: oturumunda yalnızca dev araçları ve Windows
  çalışabilir; diğerleri kapatılır (bildirime tıklayarak izin verebilirsin).
- Boştaki süreçlerin working set'ini küçültme, standby önbelleği temizleme, canlı RAM paneli.
- Düzenlenebilir kurallar, çalışan uygulamalardan öneriler, Windows ile başlatma.
- **Önizle** ile neyin durdurulacağını önceden gör.
````

- [ ] **Step 3: CI** — `.github/workflows/ci.yml`

```yaml
name: ci

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:

jobs:
  build:
    runs-on: windows-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore RamCleaner.slnx
      - run: dotnet build RamCleaner.slnx -c Release --no-restore
      - run: dotnet test RamCleaner.slnx -c Release --no-build
      - name: Publish
        if: startsWith(github.ref, 'refs/tags/v')
        shell: pwsh
        run: |
          dotnet publish src/RamCleaner.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/fd
          dotnet publish src/RamCleaner.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/sc
          Copy-Item publish/fd/RamCleaner.exe RamCleaner-win-x64.exe
          Copy-Item publish/sc/RamCleaner.exe RamCleaner-win-x64-selfcontained.exe
      - uses: softprops/action-gh-release@v2
        if: startsWith(github.ref, 'refs/tags/v')
        with:
          files: |
            RamCleaner-win-x64.exe
            RamCleaner-win-x64-selfcontained.exe
```

- [ ] **Step 4: Yerel release derlemesi**

Run: `dotnet test RamCleaner.slnx -c Release && dotnet publish src/RamCleaner.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/fd && ls -la publish/fd/RamCleaner.exe`
Expected: tüm testler PASS; `RamCleaner.exe` mevcut.

- [ ] **Step 5: Uçtan uca manuel doğrulama (kullanıcı, yönetici olarak)** — sonuçları yaz

1. Öncesi: `powershell -NoProfile -Command '$os=Get-CimInstance Win32_OperatingSystem; [math]::Round($os.FreePhysicalMemory/1MB,2)'` → boş GB'ı not et.
2. `publish/fd/RamCleaner.exe` → **Önizle** → listeyi kontrol et → **AKTİF**.
3. Spotify kapanmalı. Spotify'ı tekrar aç → ~1 sn içinde kapanmalı + tray bildirimi.
4. `sc qc DiagTrack` → `START_TYPE : 4 DISABLED`.
5. Aktifken 5+ Claude Code terminali, Docker Desktop, Android emülatörü ve Chrome aç → hiçbiri kapanmamalı; Panel'deki Claude Code oturum sayısı doğru olmalı.
6. **PASİF** → `sc qc DiagTrack` orijinal değere (genelde `2 AUTO_START`) dönmeli; `sc query DiagTrack` → `RUNNING`.
7. Tekrar **AKTİF** → Görev Yöneticisi'nden `RamCleaner`'ı sonlandır → uygulamayı yeniden aç → "servis geri yüklendi" bildirimi; `sc qc DiagTrack` orijinal değerde.
8. **Agresif** profil → Notepad aç → kapanmalı; bildirime tıkla → Notepad'i tekrar aç → açık kalmalı ve Kurallar > "izin verdiklerin" listesinde görünmeli.
9. Sonrası: 1. adımdaki komutla boş RAM'i tekrar ölç; Panel'deki "Kazanılan" ile karşılaştır.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "docs: README, MIT license and CI/release workflow"
```
