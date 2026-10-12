using System.IO;
using System.Windows;
using CCRamCleaner.Core.Config;
using CCRamCleaner.Core.Enforcement;
using CCRamCleaner.Core.Processes;
using CCRamCleaner.Core.Services;
using CCRamCleaner.Core.State;

namespace CCRamCleaner.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Global\CCRamCleaner.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);

        var log = new ActivityLog();
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CCRamCleaner");
        var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CCRamCleaner", "config.json");
        try
        {
            // The journal drives service changes made with admin rights: its folder must not be user-controlled.
            SecureDirectory.Ensure(dataDir);
        }
        catch (Exception ex) when (ex is UnsafeDirectoryException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(L.UnsafeDataFolder(dataDir, ex.Message), L.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        var processes = new WindowsProcessControl();
        var guard = new Guard(new WindowsServiceControl(), processes, new SessionJournal(Path.Combine(dataDir, "session.json")), log);

        DispatcherUnhandledException += (_, args) =>
        {
            log.Write(LogKind.Warning, args.Exception.Message);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _vm?.Shutdown();
        // Not Shutdown(): the user may cancel the logoff, and the app must keep working afterwards.
        SessionEnding += (_, _) => _vm?.OnSessionEnding();

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
