using CCRamCleaner.Core.Rules;

namespace CCRamCleaner.Core.Classification;

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
        "CCRamCleaner",
    ];

    public static bool IsProtected(string name) => NamePattern.MatchesAny(Names, name);
}
