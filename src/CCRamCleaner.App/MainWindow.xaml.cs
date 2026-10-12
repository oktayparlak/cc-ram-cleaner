using System.ComponentModel;
using System.Windows;

namespace CCRamCleaner.App;

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
