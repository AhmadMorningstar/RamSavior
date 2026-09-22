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
    private readonly Dictionary<string, Border> _swatchBorders = new();

    public Action? CompactModeChanged { get; set; }

    /// <summary>Fired whenever theme or accent color changes — MainWindow uses this to
    /// swap itself for a freshly-constructed window once this Settings dialog closes,
    /// working around a WPF-UI live-theme-switch bug rather than only partially
    /// refreshing.</summary>
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

    private void OnSwatchClicked(AccentPreset preset)
    {
        _settings.AccentColorHex = preset.Hex;
        SettingsStore.Save(_settings);
        ThemeApplier.Apply(_settings);
        HighlightSelectedSwatch();
        StatusText.Text = $"Accent set to {preset.Name}.";
        ThemeOrAccentChanged?.Invoke();
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

        _settings.Theme = sender switch
        {
            _ when ReferenceEquals(sender, LightThemeRadio) => ThemeChoice.Light,
            _ when ReferenceEquals(sender, DarkThemeRadio) => ThemeChoice.Dark,
            _ => ThemeChoice.System
        };

        SettingsStore.Save(_settings);
        ThemeApplier.Apply(_settings);
        StatusText.Text = $"Theme set to {_settings.Theme}.";
        ThemeOrAccentChanged?.Invoke();
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
