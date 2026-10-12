# RamCleaner — Tasarım (Spec)

Dev odaklı RAM koruyucu, Windows, açık kaynak (MIT). Tarih: 2026-10-12.

## Amaç
Birden fazla Claude Code terminali açıldığında 16 GB'lık makinede RAM yetmiyor (ölçüm: 15.7 GB toplam, 2.8 GB boş; Spotify ~600 MB, NVIDIA Overlay, OMEN Command Center, Riot Client, Touchpoint Analytics, SearchIndexer, telemetri servisleri arka planda). Hedef: tek butonlu bir masaüstü aracı — buton **aktifken** gereksiz Windows servislerini ve uygulamalarını durdurur ve **yeniden başlamalarını engeller**; geliştirme araçlarına (Claude Code, Docker/WSL, Android emülatörü, Chrome, IDE'ler, terminaller) asla dokunmaz. Buton kapatılınca servisler eski haline döner. Açık kaynak (MIT) yayınlanacak.

Kullanıcı kararları: **Hibrit profiller** (Dengeli = kara liste, Agresif = beyaz liste), **C# / WPF**, deaktivasyonda **servisleri geri yükle** (uygulamalar yeniden açılmaz), ekstralar: **working set trim, standby list temizleme, canlı RAM paneli**.

## Teknoloji
- **C# / WPF, .NET 10 LTS** (kullanıcı .NET 8 seçti; .NET 8 desteği 10 Kasım 2026'da bittiği için Kasım 2028'e kadar destekli .NET 10 kullanılır, API'ler aynı).
- Bağımlılıklar: `CommunityToolkit.Mvvm` (MVVM), `System.Management` (WMI), `System.ServiceProcess.ServiceController`, xUnit. Tray ikonu için ek paket yerine yerleşik WinForms `NotifyIcon` kullanılır (daha az bağımlılık). Geri kalan Win32 çağrıları P/Invoke.
- Dağıtım: framework-dependent single-file `RamCleaner.exe` + self-contained varyant (GitHub Releases).
- UI dili: İngilizce varsayılan + Türkçe; işletim sistemi diline göre seçilir. `.resx` yerine basit bir statik `L` sınıfı (dotnet CLI `.resx` tasarımcı kodunu üretmediği için).

## Mimari
```
RamCleaner.slnx   (.NET 10'un varsayılan çözüm biçimi)
src/RamCleaner.Core/        (net10.0-windows, UI yok, test edilebilir)
  Rules/        RuleSet (JSON), varsayılan kurallar (gömülü rules.default.json), kullanıcı override birleştirme
  Classification/ProcessClassifier   -> Protected | DevAllowed | UserAllowed | Blocked | Unknown
  Services/     IServiceControl + WindowsServiceControl (ServiceController + ChangeServiceConfig P/Invoke)
  Processes/    IProcessControl + WindowsProcessControl (tek süreç Kill, oturum id, üst süreç, IsProcessCritical, CPU süresi)
  Enforcement/  Guard (aktif/pasif durum makinesi), ProcessWatcher (WMI Win32_ProcessStartTrace + 2 sn poll yedeği),
                ServiceWatcher (5 sn'de bir engelli servis çalışıyor mu kontrolü)
  Memory/       MemoryMonitor (GlobalMemoryStatusEx, claude süreç sayısı), WorkingSetTrimmer (EmptyWorkingSet),
                StandbyPurger (NtSetSystemInformation / MemoryPurgeStandbyList, SeProfileSingleProcessPrivilege)
  State/        SessionJournal (write-ahead JSON: %ProgramData%\RamCleaner\session.json)
  Config/       ConfigStore (%APPDATA%\RamCleaner\config.json)
src/RamCleaner.App/         (WPF, app.manifest requireAdministrator, tray ikonu)
tests/RamCleaner.Core.Tests (xUnit, fake IServiceControl/IProcessControl — gerçek sisteme dokunmaz)
.github/workflows/ci.yml    (build + test; tag'de single-file exe release)
README.md (EN + TR), LICENSE (MIT)
```

## Davranış

**Aktifleştirme (büyük toggle buton):**
1. Önce journal'a niyet yazılır (servis adı, orijinal StartType, orijinal durum) → sonra servis `Disabled` yapılıp durdurulur. Çökme durumunda geri alınabilir.
2. Profildeki engelli süreçler öldürülür — yalnızca sürecin kendisi, ağaç değil: engelli/tanınmayan bir uygulamanın başlattığı bir dev aracı (terminal, claude, node) yanlışlıkla ölmesin. Engelli uygulamanın alt süreçleri kendi adlarıyla ayrıca sınıflandırılır.
3. Watcher'lar başlar: engelli süreç başlarsa ~1 sn içinde öldürülür; engelli servis bir şekilde çalışırsa tekrar durdurulur. Tray bildirimi: "Spotify engellendi — [İzin ver]".

**Deaktifleştirme / uygulamadan çıkış / Windows oturum kapanışı (`SessionEnding`):** journal'daki servislerin StartType ve çalışma durumu geri yüklenir, watcher'lar durur, journal silinir. Öldürülen uygulamalar yeniden açılmaz.

**Çökme kurtarma:** Uygulama açılışında journal varsa → önce geri yükleme yapılır, kullanıcıya bildirilir.

**Profiller:**
- **Dengeli:** Sadece kara liste. Varsayılan servis listesi (her biri `enabledByDefault` bayraklı): DiagTrack, dmwappushservice, WerSvc, PcaSvc, WSearch, SysMain, MapsBroker, RetailDemo, Fax, Xbl*/XboxNetApiSvc/XboxGipSvc, edgeupdate(m), gupdate(m), HP/OMEN analytics (HpTouchpointAnalyticsService vb.), AdobeARMservice, Steam Client Service. Süreçler: Spotify, Discord, Steam/steamwebhelper, EpicGamesLauncher, RiotClientServices, Battle.net, OneDrive, ms-teams, NVIDIA Overlay/nvsphelper64, OmenCommandCenterBackground, PhoneExperienceHost, Widgets/WidgetService, GameBar, Copilot, Creative Cloud yardımcıları, iCUE/Razer/ArmouryCrate/LGHUB.
- **Agresif:** Dengeli + **kullanıcı oturumundaki** (session ≠ 0) Protected / DevAllowed / UserAllowed dışındaki tüm süreçler öldürülür. Servisler her iki profilde de yalnızca kara listeyle yönetilir (rastgele servis durdurmak sistemi bozar).

**Asla dokunulmayanlar (kod içinde sabit, kullanıcı kaldıramaz):** session 0 sistem süreçleri (Agresif'te), `IsProcessCritical` olanlar, System/Registry/smss/csrss/wininit/winlogon/services/lsass/svchost/dwm/explorer/sihost/fontdrvhost/ctfmon/audiodg/Memory Compression/SearchHost/StartMenuExperienceHost/TextInputHost, antivirüs (Defender, Kaspersky avp/avpui vb.), aracın kendisi ve üst süreç zinciri.

**Varsayılan dev beyaz listesi (kullanıcı düzenleyebilir):** claude, node, bun, deno, python, java/javaw, dotnet, MSBuild, VBCSCompiler, git, ssh, bash, sh, cmd, powershell, pwsh, WindowsTerminal, OpenConsole, conhost, wsl*, wslhost, vmmem*, Docker Desktop, com.docker.*, docker*, chrome, msedge, msedgewebview2, firefox, Code, idea64, studio64, rider64, devenv, qemu-system-*, emulator*, adb.

**Ekstralar:**
- *Working set trim:* Aktifken her 60 sn'de (ayarlanabilir) son aralıkta CPU kullanmayan, ön planda olmayan süreçlere `EmptyWorkingSet`.
- *Standby temizleme:* Manuel buton + opsiyonel otomatik (boş RAM < %10 ise, en fazla 5 dk'da bir).
- *Canlı panel:* toplam/kullanılabilir RAM ve doluluk göstergesi, açık Claude Code oturumu sayısı, aktivasyondan beri kazanılan MB, son işlemler logu.

## UI (WPF, tek pencere + tray)
- Üstte büyük **AKTİF / PASİF** toggle, yanında profil seçici (Dengeli / Agresif).
- **Önizle** butonu: aktifleştirmeden önce neyin durdurulacağını ve tahmini MB kazancını listeler.
- Sekmeler: **Panel** (RAM metrikleri + log), **Kurallar** (engelli/izinli servis & süreç listeleri, checkbox'larla), **Öneriler** (şu an çalışan, sınıflandırılmamış süreçler RAM'e göre sıralı → tek tıkla engelle/izin ver), **Ayarlar** (trim aralığı, standby eşiği, Windows ile başla).
- Pencere kapanınca tray'e küçülür; tray menüsü: Aktif/Pasif, Standby temizle, Çıkış.

## Netleştirilmiş kurallar
- **Eşleştirme:** Süreç adı (`.exe` olmadan) ve servis adı, büyük/küçük harf duyarsız; `*` joker karakteri desteklenir (`com.docker.*`).
- **Sınıflandırma önceliği:** Protected (sabit) > kullanıcı İzin > kullanıcı Engel > varsayılan dev beyaz listesi > varsayılan kara liste > Unknown. Unknown süreçler Dengeli'de dokunulmaz, Agresif'te (session ≠ 0 ise) öldürülür.
- **Agresif + yeni uygulama:** Aktifken açılan sınıflandırılmamış uygulama da öldürülür; bildirimdeki "İzin ver" onu kullanıcı izin listesine ekler.
- **Servis geri yükleme:** Orijinal StartType geri yazılır; servis aktivasyondan önce çalışıyorsa yeniden başlatılır, duruyorsa durur halde bırakılır.
- **Working set trim kapsamı:** Erişilebilen tüm süreçler; hariç: ön plan penceresinin süreci, aracın kendisi, `vmmem*` (VM belleği), Protected listesi.
- **"Kazanılan MB":** Öldürülen süreçlerin öldürülme anındaki working set toplamı + kendi süreci olan (svchost dışı) durdurulan servislerin working set'i + trim ile küçülen working set miktarı. Standby temizliği sayılmaz: standby bellek zaten "kullanılabilir" sayıldığı için kazanç olarak göstermek yanıltıcı olur.
- **Windows ile başla:** `requireAdministrator` yüzünden Run anahtarı her açılışta UAC sorar; bunun yerine "en yüksek yetkiyle çalıştır" işaretli bir Task Scheduler görevi oluşturulur.

## Hata yönetimi
- Her servis/süreç işlemi bağımsız try/catch; başarısızlık (erişim reddi, korumalı süreç) loglanır, akış devam eder.
- Durdurulamayan servisler (ör. bağımlı servisler) atlanır ve panelde gösterilir.
- Journal yazılamazsa aktivasyon iptal edilir (geri alınamayacak değişiklik yapılmaz).

## Doğrulama
- `dotnet test` — ProcessClassifier (profil × liste kombinasyonları, koruma önceliği), RuleSet birleştirme, SessionJournal (yaz → çökme simülasyonu → restore), Guard durum geçişleri; tümü fake'lerle.
- Manuel (yönetici olarak):
  1. Önizle → listeyi kontrol et. Aktifleştir → Spotify kapanmalı; Spotify'ı tekrar aç → ~1 sn içinde kapanmalı + bildirim.
  2. `sc qc DiagTrack` → `START_TYPE: DISABLED`. Deaktifleştir → orijinal değere dönmeli, `sc query` ile durum kontrolü.
  3. Aktifken aracı Görev Yöneticisi'nden öldür → yeniden aç → kurtarma ile servisler geri gelmeli.
  4. Aktifken 5+ Claude Code terminali, Docker Desktop, Android emülatörü, Chrome aç → hiçbiri öldürülmemeli.
  5. Agresif profilde Notepad aç → öldürülmeli; bildirimden "İzin ver" → kalıcı izin.
  6. Boş RAM'i öncesi/sonrası ölç (panel + `Get-CimInstance Win32_OperatingSystem`).
- `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true` ile exe üretimi ve CI workflow'unun yeşil olması.
