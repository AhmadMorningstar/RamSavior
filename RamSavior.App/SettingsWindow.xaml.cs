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

    /// <summary>Lets MainWindow refresh its Custom-mode checkbox list immediately when this toggles.</summary>
    public Action? AdvancedCleaningChanged { get; set; }

    /// <summary>Lets MainWindow re-run its auto-fit sizing when compact mode changes.</summary>
    public Action? CompactModeChanged { get; set; }

    /// <summary>Lets MainWindow show/hide the Experimental section immediately.</summary>
    public Action? ExperimentalFeaturesChanged { get; set; }

    /// <summary>Lets MainWindow/App refresh the automation summary and restart the poll loop.</summary>
    public Action? AutomationChanged { get; set; }

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
        AdvancedCheckBox.IsChecked = _settings.EnableAdvancedCleaning;
        ExperimentalCheckBox.IsChecked = _settings.EnableExperimentalFeatures;

        var auto = _settings.Automation;
        AutomationEnabledCheckBox.IsChecked = auto.Enabled;
        IntervalEnabledCheckBox.IsChecked = auto.IntervalEnabled;
        IntervalMinutesBox.Text = auto.IntervalMinutes.ToString();
        FreeMemThresholdCheckBox.IsChecked = auto.FreeMemoryThresholdEnabled;
        FreeMemGbBox.Text = auto.FreeMemoryBelowGB.ToString("F1");
        LoadPercentThresholdCheckBox.IsChecked = auto.LoadPercentThresholdEnabled;
        LoadPercentBox.Text = auto.LoadAbovePercent.ToString();
        IdleMinutesBox.Text = auto.RequireIdleMinutes.ToString();
        ExcludedProcessesBox.Text = string.Join(", ", auto.ExcludedProcessNames);

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
    }

    private void CompactModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.CompactMode = CompactModeCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);
        StatusText.Text = _settings.CompactMode ? "Compact Mode enabled." : "Compact Mode disabled.";
        CompactModeChanged?.Invoke();
    }

    private void AdvancedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.EnableAdvancedCleaning = AdvancedCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);
        StatusText.Text = _settings.EnableAdvancedCleaning ? "Advanced cleaning enabled." : "Advanced cleaning disabled.";
        AdvancedCleaningChanged?.Invoke();
    }

    private void ExperimentalCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.EnableExperimentalFeatures = ExperimentalCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);
        StatusText.Text = _settings.EnableExperimentalFeatures ? "Experimental features enabled." : "Experimental features disabled.";
        ExperimentalFeaturesChanged?.Invoke();
    }

    /// <summary>
    /// Single handler for every automation field (checkboxes fire on Checked/Unchecked,
    /// text boxes on LostFocus) — reads the whole automation panel back into settings
    /// each time rather than wiring 8 separate handlers, since they all need to save +
    /// notify together anyway.
    /// </summary>
    private void AutomationSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        var auto = _settings.Automation;

        auto.Enabled = AutomationEnabledCheckBox.IsChecked == true;
        auto.IntervalEnabled = IntervalEnabledCheckBox.IsChecked == true;
        auto.IntervalMinutes = ParseIntOrDefault(IntervalMinutesBox.Text, auto.IntervalMinutes, min: 1, max: 1440);
        IntervalMinutesBox.Text = auto.IntervalMinutes.ToString();

        auto.FreeMemoryThresholdEnabled = FreeMemThresholdCheckBox.IsChecked == true;
        auto.FreeMemoryBelowGB = ParseDoubleOrDefault(FreeMemGbBox.Text, auto.FreeMemoryBelowGB, min: 0.1, max: 256);
        FreeMemGbBox.Text = auto.FreeMemoryBelowGB.ToString("F1");

        auto.LoadPercentThresholdEnabled = LoadPercentThresholdCheckBox.IsChecked == true;
        auto.LoadAbovePercent = ParseIntOrDefault(LoadPercentBox.Text, auto.LoadAbovePercent, min: 1, max: 99);
        LoadPercentBox.Text = auto.LoadAbovePercent.ToString();

        auto.RequireIdleMinutes = ParseIntOrDefault(IdleMinutesBox.Text, auto.RequireIdleMinutes, min: 0, max: 1440);
        IdleMinutesBox.Text = auto.RequireIdleMinutes.ToString();

        auto.ExcludedProcessNames = ExcludedProcessesBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        SettingsStore.Save(_settings);
        StatusText.Text = auto.Enabled ? "Automation settings saved." : "Automation is off.";
        AutomationChanged?.Invoke();
    }

    private static int ParseIntOrDefault(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out int value)) return fallback;
        return Math.Clamp(value, min, max);
    }

    private static double ParseDoubleOrDefault(string text, double fallback, double min, double max)
    {
        if (!double.TryParse(text, out double value)) return fallback;
        return Math.Clamp(value, min, max);
    }
}
