using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RamSavior.App.Settings;
using RamSavior.Core.Engine;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class AutomationConfigWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private bool _isLoaded;
    private readonly Dictionary<MemoryListCommand, System.Windows.Controls.CheckBox> _autoCustomCheckboxes = new();

    /// <summary>Fired on every save — MainWindow uses this to refresh its Automation
    /// quick-card summary and restart the background poll loop with the new settings.</summary>
    public Action? AutomationChanged { get; set; }

    public AutomationConfigWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        BuildAutoCustomItemsPanel();

        _isLoaded = false;

        var auto = _settings.Automation;
        AutomationEnabledCheckBox.IsChecked = auto.Enabled;
        IntervalEnabledCheckBox.IsChecked = auto.IntervalEnabled;
        IntervalMinutesBox.Text = auto.IntervalMinutes.ToString();
        FreeMemThresholdCheckBox.IsChecked = auto.FreeMemoryThresholdEnabled;
        FreeMemGbBox.Text = auto.FreeMemoryBelowGB.ToString("F1");
        LoadPercentThresholdCheckBox.IsChecked = auto.LoadPercentThresholdEnabled;
        LoadPercentBox.Text = auto.LoadAbovePercent.ToString();
        StandbyThresholdCheckBox.IsChecked = auto.StandbyListThresholdEnabled;
        StandbyMbBox.Text = auto.StandbyListAboveMB.ToString("F0");
        TimeOfDayCheckBox.IsChecked = auto.TimeOfDayEnabled;
        TimeOfDayBox.Text = auto.TimeOfDay.ToString(@"hh\:mm");
        PerProcessAutoTrimCheckBox.IsChecked = auto.PerProcessAutoTrimEnabled;
        PerProcessMbBox.Text = auto.PerProcessAutoTrimAboveMB.ToString("F0");
        IdleMinutesBox.Text = auto.RequireIdleMinutes.ToString();
        ExcludedProcessesBox.Text = string.Join(", ", auto.ExcludedProcessNames);

        (_settings.AutomationTier switch
        {
            CleanMode.Moderate => AutoTierModerateRadio,
            CleanMode.Custom => AutoTierCustomRadio,
            _ => AutoTierNormalRadio
        }).IsChecked = true;

        foreach (var (command, checkBox) in _autoCustomCheckboxes)
            checkBox.IsChecked = _settings.AutomationCustomItems.Contains(command);

        ApplyAutoTierAvailability();

        _isLoaded = true;
    }

    /// <summary>
    /// Builds the "what automation cleans" checklist the same way MainWindow builds its
    /// Custom-tier picker — same titles, same descriptions, same ADVANCED badges — so a
    /// user who has already learned that panel doesn't need to relearn a second one here.
    /// </summary>
    private void BuildAutoCustomItemsPanel()
    {
        AutoCustomItemsPanel.Children.Clear();
        _autoCustomCheckboxes.Clear();

        foreach (var info in MemoryCommandCatalog.All)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var headerRow = new DockPanel();

            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = info.Title,
                FontWeight = FontWeights.SemiBold,
                ToolTip = info.Description
            };
            checkBox.Checked += AutoCustomItem_Changed;
            checkBox.Unchecked += AutoCustomItem_Changed;
            DockPanel.SetDock(checkBox, Dock.Left);
            headerRow.Children.Add(checkBox);

            if (info.IsAdvanced)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x7A, 0x1A)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = "ADVANCED",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.White
                    }
                };
                headerRow.Children.Add(badge);
            }

            var description = new System.Windows.Controls.TextBlock
            {
                Text = info.Description,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 2, 0, 0)
            };

            container.Children.Add(headerRow);
            container.Children.Add(description);
            AutoCustomItemsPanel.Children.Add(container);

            _autoCustomCheckboxes[info.Command] = checkBox;
        }
    }

    private void AutoCustomItem_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.AutomationCustomItems = _autoCustomCheckboxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();

        SettingsStore.Save(_settings);
        StatusText.Text = "Automation item selection saved.";
        AutomationChanged?.Invoke();
    }

    private void AutomationTierRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.AutomationTier = sender switch
        {
            _ when ReferenceEquals(sender, AutoTierModerateRadio) => CleanMode.Moderate,
            _ when ReferenceEquals(sender, AutoTierCustomRadio) => CleanMode.Custom,
            _ => CleanMode.Normal
        };

        SettingsStore.Save(_settings);
        ApplyAutoTierAvailability();
        StatusText.Text = $"Automation now cleans using: {_settings.AutomationTier}.";
        AutomationChanged?.Invoke();
    }

    /// <summary>
    /// Custom is only selectable while the main window is in Advanced or Experimental
    /// mode — mirrors the same safety gate AutomationController enforces at run time, so
    /// this window never lets a user configure something that won't actually take effect.
    /// </summary>
    private void ApplyAutoTierAvailability()
    {
        bool unlocked = _settings.EnableAdvancedCleaning;

        AutoTierCustomRadio.IsEnabled = unlocked;
        AutoTierCustomLockedText.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;

        if (!unlocked && AutoTierCustomRadio.IsChecked == true)
            AutoTierNormalRadio.IsChecked = true; // also flips _settings.AutomationTier via the handler above

        AutoCustomPanel.Visibility = unlocked && AutoTierCustomRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var checkBox in _autoCustomCheckboxes.Values)
            checkBox.IsEnabled = unlocked;
    }

    /// <summary>
    /// Single handler for every automation field (checkboxes fire on Checked/Unchecked,
    /// text boxes on LostFocus) — reads the whole panel back into settings each time
    /// rather than wiring many separate handlers, since they all need to save + notify
    /// together anyway.
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

        auto.StandbyListThresholdEnabled = StandbyThresholdCheckBox.IsChecked == true;
        auto.StandbyListAboveMB = ParseDoubleOrDefault(StandbyMbBox.Text, auto.StandbyListAboveMB, min: 64, max: 262144);
        StandbyMbBox.Text = auto.StandbyListAboveMB.ToString("F0");

        auto.TimeOfDayEnabled = TimeOfDayCheckBox.IsChecked == true;
        auto.TimeOfDay = ParseTimeOfDayOrDefault(TimeOfDayBox.Text, auto.TimeOfDay);
        TimeOfDayBox.Text = auto.TimeOfDay.ToString(@"hh\:mm");

        auto.PerProcessAutoTrimEnabled = PerProcessAutoTrimCheckBox.IsChecked == true;
        auto.PerProcessAutoTrimAboveMB = ParseDoubleOrDefault(PerProcessMbBox.Text, auto.PerProcessAutoTrimAboveMB, min: 50, max: 65536);
        PerProcessMbBox.Text = auto.PerProcessAutoTrimAboveMB.ToString("F0");

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

    private static TimeSpan ParseTimeOfDayOrDefault(string text, TimeSpan fallback)
    {
        return TimeSpan.TryParse(text, out var value) && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1)
            ? value
            : fallback;
    }
}
