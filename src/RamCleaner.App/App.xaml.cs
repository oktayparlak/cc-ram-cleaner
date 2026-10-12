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
