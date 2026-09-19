using System.Windows;
using RamSavior.App.Settings;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class LayoutWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private bool _isLoaded;

    /// <summary>Fired live on every toggle so the main window can apply visibility
    /// immediately, the same pattern SettingsWindow already uses for its callbacks.</summary>
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
}
