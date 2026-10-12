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
        menu.Items.Add(L.ToggleMenu, null, (_, _) =>
        {
            // AsyncRelayCommand.Execute ignores CanExecute; never start a second toggle while one is running.
            if (vm.ToggleCommand.CanExecute(null)) vm.ToggleCommand.Execute(null);
        });
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
