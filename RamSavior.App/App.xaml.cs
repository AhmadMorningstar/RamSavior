using System.ComponentModel;
using System.Windows;
using RamSavior.App.Automation;
using RamSavior.App.Settings;
using RamSavior.App.Tray;

namespace RamSavior.App;

public partial class App : System.Windows.Application
{
    private AppSettings _settings = null!;
    private AutomationController _automationController = null!;
    private TrayIconManager _tray = null!;
    private MainWindow _mainWindow = null!;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Automation needs the app to keep running even if every window is closed —
        // ShutdownMode="OnMainWindowClose" (set in App.xaml) would defeat that, so we
        // override it here and control shutdown explicitly via the tray "Exit" item.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = SettingsStore.Load();
        ThemeApplier.Apply(_settings);

        _mainWindow = new MainWindow(_settings);
        MainWindow = _mainWindow;
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.Show();

        _tray = new TrayIconManager();
        _tray.OpenRequested += () => Dispatcher.Invoke(ShowMainWindow);
        _tray.CleanNowRequested += () => _mainWindow.TriggerQuickClean();
        _tray.ExitRequested += () => Dispatcher.Invoke(ExitApplication);

        _automationController = new AutomationController(_settings);
        _automationController.CleanupFired += result =>
        {
            Dispatcher.Invoke(() =>
            {
                _mainWindow.NotifyAutomationRanInBackground(result);
                if (result.Success)
                    _tray.ShowBalloon("RAM Savior", $"Automatically freed {result.FreedGB:F2} GB.");
            });
        };

        if (_settings.Automation.Enabled)
            _automationController.Start();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;

        // Closing the window hides it to tray instead of exiting — this is what lets
        // "clean every N minutes" actually mean something. First time this happens,
        // let the user know where the app went.
        e.Cancel = true;
        _mainWindow.Hide();

        if (!_settings.HasShownTrayHint)
        {
            _tray.ShowBalloon("RAM Savior is still running", "It's here in the tray — automation keeps working in the background. Right-click the icon to exit.");
            _settings.HasShownTrayHint = true;
            SettingsStore.Save(_settings);
        }
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ExitApplication()
    {
        _isExiting = true;
        _automationController.Stop();
        _tray.Dispose();
        Shutdown();
    }

    /// <summary>Called after Settings changes automation config — restarts the poll loop with fresh config.</summary>
    public void RestartAutomationIfNeeded()
    {
        _automationController.Stop();
        if (_settings.Automation.Enabled)
            _automationController.Start();
    }
}
