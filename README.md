# CCRamCleaner

One button that frees RAM for development on Windows. While **active** it stops background bloat
(telemetry services, game launchers, OEM helpers, chat/music apps…) and keeps it from coming back —
without touching your dev tools (Claude Code, Node, Python, Java, Docker/WSL, Android emulator,
Chrome/Edge, VS Code, JetBrains IDEs, terminals). Turn it off and every service is restored.

> 🇹🇷 Türkçe açıklama aşağıda.

## Features
- **Balanced** profile: blocks a curated list of known bloat. **Aggressive** profile: only dev tools and
  Windows itself may run in your session; anything else is closed (click the notification to allow it).
- Services are disabled while active and restored on deactivate, exit, logoff — and after a crash
  (write-ahead journal in `%ProgramData%\CCRamCleaner\session.json`).
- Idle working-set trimming, standby-cache purge, live RAM dashboard with Claude Code session count.
- Editable rules, suggestions from currently running apps, start with Windows.

## Install
Download `CCRamCleaner-win-x64.exe` (needs the .NET 10 Desktop Runtime) or
`CCRamCleaner-win-x64-selfcontained.exe` from Releases. The app asks for administrator rights
(needed to stop services and other processes).

## Safety
Windows core processes, security software, critical processes, processes it cannot inspect, and
CCRamCleaner's own parent chain are never touched. Services are only changed if they are on the
block list; the original start type is recorded *before* any change. Use **Preview** to see exactly
what will be stopped.

## Build
```bash
dotnet test CCRamCleaner.slnx
dotnet publish src/CCRamCleaner.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
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
