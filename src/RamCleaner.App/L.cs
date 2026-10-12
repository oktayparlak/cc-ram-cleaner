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
    public static string AgentSessions => T("AI agent sessions", "AI ajan oturumu");
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
    public static string UnsafeDataFolder(string path, string reason) => T(
        $"RamCleaner cannot use its data folder safely and will close.\n\n{reason}\n\nDelete \"{path}\" (as administrator) and start RamCleaner again.",
        $"RamCleaner veri klasörünü güvenle kullanamıyor ve kapanacak.\n\n{reason}\n\n\"{path}\" klasörünü (yönetici olarak) silip RamCleaner'ı yeniden başlat.");
    public static string UnsafePattern(string pattern) => T($"'{pattern}' is too broad to block", $"'{pattern}' engellemek için fazla geniş");
}
