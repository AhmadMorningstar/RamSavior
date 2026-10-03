using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RamSavior.App.Settings;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class SettingsWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private bool _isLoaded;
    private readonly Dictionary<string, Border> _swatchBorders = new(StringComparer.OrdinalIgnoreCase);

    public Action? CompactModeChanged { get; set; }

    /// <summary>Fired (from PerformSelfReplacement, after the replacement Settings window
    /// has rendered — not immediately) whenever theme or accent color changes. MainWindow
    /// uses this to swap itself for a freshly-constructed window too, working around a
    /// WPF-UI live-theme-switch bug rather than only partially refreshing.</summary>
    public Action? ThemeOrAccentChanged { get; set; }

    /// <summary>Fired whenever the icon bar position changes, so MainWindow can reparent
    /// it into the new slot immediately.</summary>
    public Action? IconBarPositionChanged { get; set; }

    /// <summary>Automation itself is configured in the dedicated Automation Configuration
    /// window now — this is kept only because Reset Everything still needs to notify
    /// MainWindow that automation was turned off.</summary>
    public Action? AutomationChanged { get; set; }
    public Action? StartWithWindowsChanged { get; set; }
    public Action? GlobalHotkeyChanged { get; set; }

    private static readonly IconBarPosition[] IconBarPositionOrder =
    {
        IconBarPosition.TopRight, IconBarPosition.TopCenter, IconBarPosition.TopLeft,
        IconBarPosition.BottomRight, IconBarPosition.BottomCenter, IconBarPosition.BottomLeft
    };

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Icon = AppIcons.Window;
        _settings = settings;

        BuildSwatches();

        _isLoaded = false;
        (_settings.Theme switch
        {
            ThemeChoice.Light => LightThemeRadio,
            ThemeChoice.Dark => DarkThemeRadio,
            _ => SystemThemeRadio
        }).IsChecked = true;

        CompactModeCheckBox.IsChecked = _settings.CompactMode;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        GlobalHotkeyCheckBox.IsChecked = _settings.GlobalHotkeyEnabled;

        int iconBarIdx = Array.IndexOf(IconBarPositionOrder, _settings.Layout.IconBarPosition);
        IconBarPositionCombo.SelectedIndex = iconBarIdx >= 0 ? iconBarIdx : 0;

        _isLoaded = true;

        HighlightSelectedSwatch();
        ConfigureCustomAccentSection();
    }

    private void BuildSwatches()
    {
        foreach (var preset in AccentPresets.All)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(preset.Hex)!;

            var swatch = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = preset.Name
            };

            swatch.MouseLeftButtonUp += (_, _) => OnSwatchClicked(preset);
            _swatchBorders[preset.Hex] = swatch;
            AccentSwatches.Items.Add(swatch);
        }
    }

    private void OnSwatchClicked(AccentPreset preset) => ApplyAccent(preset.Hex, $"{preset.Name} accent");

    /// <summary>Single path for every accent change (preset, custom, reset). Nothing on
    /// screen changes until the user confirms the restart prompt.</summary>
    private void ApplyAccent(string hex, string description)
    {
        if (ThemeChangeFlow.RequestAccentChange(this, _settings, hex))
        {
            PerformSelfReplacement();
            return;
        }

        // Not restarted (yet): reflect the saved choice in this window without touching the theme.
        HighlightSelectedSwatch();
        ConfigureCustomAccentSection();
        StatusText.Text = ThemeApplier.IsPendingRestart(_settings)
            ? $"{description} saved \u2014 it will apply after a restart."
            : $"{description} set.";
    }

    /// <summary>Custom accent is Experimental-only. Never hidden - greyed out otherwise, so
    /// everyone can see it exists. (Settings is modal, so the mode can't change while open.)</summary>
    private void ConfigureCustomAccentSection()
    {
        bool unlocked = _settings.EnableExperimentalFeatures;

        CustomAccentControls.IsEnabled = unlocked;
        CustomAccentControls.Opacity = unlocked ? 1.0 : 0.4;
        CustomAccentLockNote.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
        CustomAccentCard.ToolTip = unlocked ? null : "Available in Experimental mode";

        string hex = _settings.AccentColorHex;
        CurrentAccentSwatch.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        CurrentAccentText.Text = $"Current: {AccentPresets.NameFor(hex) ?? "Custom"} ({hex.ToUpperInvariant()})"
            + (ThemeApplier.IsPendingRestart(_settings) ? " \u2014 applies after restart" : "");

        // Nothing to reset when it's already the default.
        if (unlocked)
            ResetAccentButton.IsEnabled = !string.Equals(hex, AccentPresets.DefaultHex, StringComparison.OrdinalIgnoreCase);
    }

    private void CustomAccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableExperimentalFeatures) return;

        var picker = new CustomAccentWindow(_settings.AccentColorHex) { Owner = this };
        picker.ShowDialog();

        if (picker.ChosenHex is { } hex &&
            !string.Equals(hex, _settings.AccentColorHex, StringComparison.OrdinalIgnoreCase))
        {
            ApplyAccent(hex, $"Custom accent {hex}");
        }
    }

    private void ResetAccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableExperimentalFeatures) return;
        ApplyAccent(AccentPresets.DefaultHex, "Default Amber accent");
    }

    private void HighlightSelectedSwatch()
    {
        foreach (var (hex, border) in _swatchBorders)
        {
            border.BorderBrush = hex == _settings.AccentColorHex
                ? System.Windows.Media.Brushes.White
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        ThemeChoice choice = sender switch
        {
            _ when ReferenceEquals(sender, LightThemeRadio) => ThemeChoice.Light,
            _ when ReferenceEquals(sender, DarkThemeRadio) => ThemeChoice.Dark,
            _ => ThemeChoice.System
        };

        // Nothing on screen changes here. The choice is saved and the user is asked to
        // restart; only "Restart now" applies it (and rebuilds this window + the main one).
        if (ThemeChangeFlow.RequestChange(this, _settings, choice))
        {
            PerformSelfReplacement();
            return;
        }

        StatusText.Text = ThemeApplier.IsPendingRestart(_settings)
            ? $"{choice} theme saved \u2014 it will apply after a restart."
            : $"Theme set to {choice}.";
    }

    /// <summary>
    /// This window is just as subject to WPF-UI's live-theme-switch corruption as the
    /// main window was (same underlying bug — lepoco/wpfui#927/#1193) since it's an
    /// already-open window having its theme swapped in place. Rather than only fix the
    /// main window and leave this one still glitching, apply the exact same remedy here:
    /// replace this window with a freshly-constructed one at the same position/size.
    ///
    /// The main-window swap (ThemeOrAccentChanged) is deliberately NOT fired here
    /// directly — it's deferred to newWindow's ContentRendered event, so this
    /// replacement Settings window visibly appears and finishes rendering first, and the
    /// main window only swaps a beat after. newWindow.Activate() afterward then brings
    /// Settings back on top of whatever the main window's own swap just showed, so it
    /// ends up front-most either way — Settings lost the Owner-enforced "can never be
    /// covered by its owner" guarantee when Owner was removed to let the main window
    /// swap while this stays open, so this restores that ordering by hand.
    /// </summary>
    private void PerformSelfReplacement()
    {
        double left = Left, top = Top, width = Width, height = Height;

        var newWindow = new SettingsWindow(_settings)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
        MainWindow.WireSettingsCallbacks(newWindow);

        newWindow.ContentRendered += (_, _) =>
        {
            ThemeOrAccentChanged?.Invoke();
            newWindow.Activate();
        };

        Close();

        newWindow.ShowDialog();
    }

    private void CompactModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.CompactMode = CompactModeCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);
        StatusText.Text = _settings.CompactMode ? "Compact Mode enabled." : "Compact Mode disabled.";
        CompactModeChanged?.Invoke();
    }

    private void IconBarPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        int idx = IconBarPositionCombo.SelectedIndex;
        if (idx < 0 || idx >= IconBarPositionOrder.Length) return;

        _settings.Layout.IconBarPosition = IconBarPositionOrder[idx];
        SettingsStore.Save(_settings);

        StatusText.Text = $"Icon bar moved to {(string)((ComboBoxItem)IconBarPositionCombo.SelectedItem).Content}.";
        IconBarPositionChanged?.Invoke();
    }

    private void StartupSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        bool startChanged = _settings.StartWithWindows != (StartWithWindowsCheckBox.IsChecked == true);
        bool hotkeyChanged = _settings.GlobalHotkeyEnabled != (GlobalHotkeyCheckBox.IsChecked == true);

        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.GlobalHotkeyEnabled = GlobalHotkeyCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);

        StatusText.Text = "Startup settings saved.";
        if (startChanged) StartWithWindowsChanged?.Invoke();
        if (hotkeyChanged) GlobalHotkeyChanged?.Invoke();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "This turns off automation, removes the startup task and hotkey, and deletes all saved settings and history. Continue?",
            "Reset RAM Savior",
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        _settings.Automation.Enabled = false;
        _settings.StartWithWindows = false;
        _settings.GlobalHotkeyEnabled = false;

        AutomationChanged?.Invoke();
        StartWithWindowsChanged?.Invoke();
        GlobalHotkeyChanged?.Invoke();

        SettingsStore.ResetToDefaultsAndDeleteHistory();

        StatusText.Text = "Reset complete. Restart RAM Savior to fully apply defaults.";
    }
}
