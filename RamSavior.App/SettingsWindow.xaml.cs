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
        _isLoaded = true;

        HighlightSelectedSwatch();
    }

    private void BuildSwatches()
    {
        foreach (var preset in AccentPresets.All)
        {
            var color = (Color)ColorConverter.ConvertFromString(preset.Hex)!;

            var swatch = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
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
                ? Brushes.White
                : Brushes.Transparent;
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
}
