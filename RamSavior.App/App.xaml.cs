using System.Windows;
using RamSavior.App.Settings;

namespace RamSavior.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsStore.Load();
        ThemeApplier.Apply(settings);

        // Explicit window creation (rather than relying on Application's StartupUri)
        // so we have a hook later for tray-icon / "start minimized" launch options.
        var mainWindow = new MainWindow(settings);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
