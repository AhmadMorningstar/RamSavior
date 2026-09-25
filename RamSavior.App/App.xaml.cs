using System.ComponentModel;
using System.Windows;
using RamSavior.App.Automation;
using RamSavior.App.HotKey;
using RamSavior.App.Settings;
using RamSavior.App.Tray;
using RamSavior.Core.Automation;

namespace RamSavior.App;

public partial class App : System.Windows.Application
{
    private const string StartupTaskName = "RamSaviorStartup";

    private AppSettings _settings = null!;
    private AutomationController _automationController = null!;
    private TrayIconManager _tray = null!;
    private MainWindow _mainWindow = null!;
    private GlobalHotKeyManager? _hotKeyManager;
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

        if (_settings.GlobalHotkeyEnabled)
            TryRegisterHotKey();

        SyncStartWithWindowsTask();

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
        _automationController.ProcessAutoTrimmed += (name, mb) =>
        {
            Dispatcher.Invoke(() => _tray.ShowBalloon("RAM Savior", $"Trimmed {name} (was {mb:F0} MB)."));
        };

        if (_settings.Automation.Enabled)
            _automationController.Start();

        if (!_settings.HasSeenWelcome)
        {
            var welcome = new WelcomeWindow { Owner = _mainWindow };
            welcome.ShowDialog();
            _settings.HasSeenWelcome = welcome.DontShowAgain;
            SettingsStore.Save(_settings);
        }
    }

    private void TryRegisterHotKey()
    {
        try
        {
            _hotKeyManager = new GlobalHotKeyManager(_mainWindow);
            _hotKeyManager.HotKeyPressed += () => _mainWindow.TriggerQuickClean();
            _hotKeyManager.Register();
        }
        catch
        {
            // Non-fatal — the app works fine without the hotkey; another app may already
            // own Ctrl+Alt+R, which isn't worth surfacing as an error.
        }
    }

    /// <summary>Called from Settings when the global hotkey toggle changes.</summary>
    public void RefreshHotKey()
    {
        _hotKeyManager?.Dispose();
        _hotKeyManager = null;
        if (_settings.GlobalHotkeyEnabled)
            TryRegisterHotKey();
    }

    private void SyncStartWithWindowsTask()
    {
        string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        if (_settings.StartWithWindows)
            TaskSchedulerIntegration.InstallStartupTask(StartupTaskName, exePath);
        else if (TaskSchedulerIntegration.TaskExists(StartupTaskName))
            TaskSchedulerIntegration.RemoveTask(StartupTaskName);
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
        _hotKeyManager?.Dispose();
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

    /// <summary>Called after Settings changes the Start-with-Windows toggle.</summary>
    public void RefreshStartWithWindowsTask() => SyncStartWithWindowsTask();

    /// <summary>The live main window. Settings resolves this fresh every time it needs to
    /// act on it (rather than holding its own reference from when it was opened) — since
    /// theme changes now swap the main window out immediately while Settings stays open
    /// and unowned, a captured reference would go stale after the first swap.</summary>
    internal MainWindow CurrentMainWindow => _mainWindow;

    /// <summary>
    /// Swaps in a freshly-constructed MainWindow and retires the old one — used for
    /// theme/accent changes. WPF-UI's live in-place theme switching is unreliable after
    /// more than one switch (documented library bugs: lepoco/wpfui#927, #1193 — the Mica
    /// backdrop composition gets out of sync with the DWM dark-mode flag), and a plain
    /// resource re-apply doesn't reliably clear that up. A brand new window always
    /// renders correctly against the current theme, which is why re-opening Settings
    /// always "fixed" it — so lean into that instead of fighting the live-switch path.
    /// Every place that references the old window (tray callbacks, the automation
    /// callback, the global hotkey) captures the `_mainWindow` field directly rather than
    /// a snapshot, so updating the field here is enough to redirect all of them.
    /// </summary>
    internal void ReplaceMainWindow(MainWindow oldWindow, MainWindow newWindow)
    {
        oldWindow.Closing -= MainWindow_Closing;

        _mainWindow = newWindow;
        MainWindow = newWindow;
        _mainWindow.Closing += MainWindow_Closing;

        // The hotkey is registered against a specific window's HWND — re-point it at the
        // new window now that it exists and has been shown (has a valid HWND).
        RefreshHotKey();

        // No handler attached anymore, so this actually closes rather than hiding to tray.
        oldWindow.Close();
    }
}
