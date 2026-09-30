using System.Windows;
using System.Windows.Controls;
using RamSavior.App.Settings;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class LayoutWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private bool _isLoaded;

    /// <summary>Fired live on every change so the main window can apply it immediately —
    /// visibility, arrangement mode, or a loaded/reset saved layout.</summary>
    public Action? LayoutChanged { get; set; }

    public LayoutWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _isLoaded = false;
        LoadFromSettings();
        _isLoaded = true;
    }

    private void LoadFromSettings()
    {
        var layout = _settings.Layout;
        MemoryStatusCheckBox.IsChecked = layout.ShowMemoryStatus;
        InsightsCheckBox.IsChecked = layout.ShowInsights;
        AutomationCheckBox.IsChecked = layout.ShowAutomation;
        CleanCheckBox.IsChecked = layout.ShowClean;
        FocusModeCheckBox.IsChecked = layout.ShowFocusMode;
        PerProcessTrimCheckBox.IsChecked = layout.ShowPerProcessTrim;

        bool experimental = _settings.EnableExperimentalFeatures;
        CustomArrangeRadio.IsEnabled = experimental;
        ArrangeLockedText.Visibility = experimental ? Visibility.Collapsed : Visibility.Visible;

        (layout.UseCustomArrangement && experimental ? CustomArrangeRadio : AutoArrangeRadio).IsChecked = true;
        SavedArrangementsPanel.Visibility = layout.UseCustomArrangement && experimental ? Visibility.Visible : Visibility.Collapsed;

        RefreshSavedArrangementsCombo();
    }

    private void RefreshSavedArrangementsCombo()
    {
        SavedArrangementsCombo.SelectionChanged -= SavedArrangementsCombo_SelectionChanged;

        SavedArrangementsCombo.Items.Clear();
        SavedArrangementsCombo.Items.Add("(current / unsaved)");
        foreach (var name in _settings.Layout.SavedArrangements.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            SavedArrangementsCombo.Items.Add(name);
        SavedArrangementsCombo.SelectedIndex = 0;

        SavedArrangementsCombo.SelectionChanged += SavedArrangementsCombo_SelectionChanged;
    }

    private void LayoutSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        var layout = _settings.Layout;
        layout.ShowMemoryStatus = MemoryStatusCheckBox.IsChecked == true;
        layout.ShowInsights = InsightsCheckBox.IsChecked == true;
        layout.ShowAutomation = AutomationCheckBox.IsChecked == true;
        layout.ShowClean = CleanCheckBox.IsChecked == true;
        layout.ShowFocusMode = FocusModeCheckBox.IsChecked == true;
        layout.ShowPerProcessTrim = PerProcessTrimCheckBox.IsChecked == true;

        SettingsStore.Save(_settings);
        StatusText.Text = "Layout saved.";
        LayoutChanged?.Invoke();
    }

    private void ShowEverythingButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        MemoryStatusCheckBox.IsChecked = true;
        InsightsCheckBox.IsChecked = true;
        AutomationCheckBox.IsChecked = true;
        CleanCheckBox.IsChecked = true;
        FocusModeCheckBox.IsChecked = true;
        PerProcessTrimCheckBox.IsChecked = true;
        _isLoaded = true;

        LayoutSetting_Changed(sender, e);
    }

    // ----- Arrangement: Automatic vs Custom -----

    private void ArrangeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.Layout.UseCustomArrangement = ReferenceEquals(sender, CustomArrangeRadio);
        SettingsStore.Save(_settings);

        SavedArrangementsPanel.Visibility = _settings.Layout.UseCustomArrangement ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _settings.Layout.UseCustomArrangement
            ? "Custom arrangement on — drag any section by its title to move it, or its bottom-right corner to resize it."
            : "Back to automatic reflow.";

        LayoutChanged?.Invoke();
    }

    private void SaveArrangementButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAsPrompt.Visibility = Visibility.Visible;
        SaveAsNameBox.Text = "";
        SaveAsNameBox.Focus();
    }

    private void ConfirmSaveArrangementButton_Click(object sender, RoutedEventArgs e)
    {
        string name = SaveAsNameBox.Text.Trim();
        if (name.Length == 0 || name == "(current / unsaved)") return;

        _settings.Layout.SavedArrangements[name] = new SavedArrangement
        {
            Positions = _settings.Layout.Positions.ToDictionary(
                kv => kv.Key,
                kv => new PanelBounds { X = kv.Value.X, Y = kv.Value.Y, Width = kv.Value.Width, Height = kv.Value.Height })
        };
        SettingsStore.Save(_settings);

        SaveAsPrompt.Visibility = Visibility.Collapsed;
        RefreshSavedArrangementsCombo();
        SavedArrangementsCombo.SelectedItem = name;
        StatusText.Text = $"Saved layout \"{name}\".";
    }

    private void SavedArrangementsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (SavedArrangementsCombo.SelectedItem is not string name || name == "(current / unsaved)") return;
        if (!_settings.Layout.SavedArrangements.TryGetValue(name, out var saved)) return;

        _settings.Layout.Positions = saved.Positions.ToDictionary(
            kv => kv.Key,
            kv => new PanelBounds { X = kv.Value.X, Y = kv.Value.Y, Width = kv.Value.Width, Height = kv.Value.Height });
        SettingsStore.Save(_settings);

        StatusText.Text = $"Loaded layout \"{name}\".";
        LayoutChanged?.Invoke();
    }

    private void DeleteArrangementButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedArrangementsCombo.SelectedItem is not string name || name == "(current / unsaved)") return;

        _settings.Layout.SavedArrangements.Remove(name);
        SettingsStore.Save(_settings);
        RefreshSavedArrangementsCombo();
        StatusText.Text = $"Deleted layout \"{name}\".";
    }

    private void ResetArrangementButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Layout.Positions.Clear();
        SettingsStore.Save(_settings);

        StatusText.Text = "Positions reset — sections will cascade back to their default spots.";
        LayoutChanged?.Invoke();
    }
}
