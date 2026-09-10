using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RamSavior.App.Settings;
using RamSavior.Core.Engine;
using RamSavior.Core.Monitoring;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _pollTimer;
    private readonly AppSettings _settings;
    private readonly Dictionary<MemoryListCommand, CheckBox> _customCheckboxes = new();
    private readonly Dictionary<MemoryListCommand, StackPanel> _customRowContainers = new();

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        BuildCustomItemsPanel();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshStatus();
        _pollTimer.Start();

        RefreshStatus();
    }

    private void BuildCustomItemsPanel()
    {
        CustomItemsPanel.Children.Clear();
        _customCheckboxes.Clear();
        _customRowContainers.Clear();

        foreach (var info in MemoryCommandCatalog.All)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var headerRow = new DockPanel();

            var checkBox = new CheckBox
            {
                Content = info.Title,
                FontWeight = FontWeights.SemiBold
            };
            DockPanel.SetDock(checkBox, Dock.Left);
            headerRow.Children.Add(checkBox);

            if (info.IsAdvanced)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x7A, 0x1A)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = "ADVANCED",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
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
            CustomItemsPanel.Children.Add(container);

            _customCheckboxes[info.Command] = checkBox;
            _customRowContainers[info.Command] = container;
        }

        ApplyAdvancedGating();
    }

    /// <summary>Enables/disables + dims the Advanced-tier checkboxes based on the current setting.</summary>
    private void ApplyAdvancedGating()
    {
        foreach (var info in MemoryCommandCatalog.All)
        {
            if (!info.IsAdvanced) continue;

            var checkBox = _customCheckboxes[info.Command];
            var container = _customRowContainers[info.Command];

            checkBox.IsEnabled = _settings.EnableAdvancedCleaning;
            if (!_settings.EnableAdvancedCleaning) checkBox.IsChecked = false;

            checkBox.ToolTip = _settings.EnableAdvancedCleaning
                ? null
                : "Enable Advanced Cleaning in Settings to use this option.";

            container.Opacity = _settings.EnableAdvancedCleaning ? 1.0 : 0.5;
        }
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        // Guard against firing before InitializeComponent has fully wired every named
        // element — SmartModeRadio's IsChecked="True" in XAML fires this Checked event
        // mid-load, before CustomPanel itself has been connected yet.
        if (CustomPanel is null) return;

        CustomPanel.Visibility = CustomModeRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        settingsWindow.AdvancedCleaningChanged = ApplyAdvancedGating;
        settingsWindow.ShowDialog();
    }

    private void RefreshStatus()
    {
        var reading = MemoryStatus.Read();

        AvailableText.Text = $"{reading.AvailablePhysicalGB:F2} GB available";
        TotalText.Text = $"of {reading.TotalPhysicalGB:F2} GB total";
        LoadBar.Value = reading.MemoryLoadPercent;
        LoadPercentText.Text = $"{reading.MemoryLoadPercent}% memory load";
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        Func<CleanupResult> runAction;

        if (CustomModeRadio.IsChecked == true)
        {
            var selected = _customCheckboxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToHashSet();

            if (selected.Count == 0)
            {
                ResultText.Text = "Select at least one item to clean.";
                return;
            }

            runAction = () => CleanupEngine.RunCustom(selected);
        }
        else
        {
            var mode = FullModeRadio.IsChecked == true ? CleanMode.Full : CleanMode.Smart;
            runAction = () => CleanupEngine.Run(mode);
        }

        CleanButton.IsEnabled = false;
        ResultText.Text = "Cleaning...";

        Task.Run(runAction).ContinueWith(t =>
        {
            var result = t.Result;

            Dispatcher.Invoke(() =>
            {
                CleanButton.IsEnabled = true;
                RefreshStatus();

                ResultText.Text = result.Success
                    ? $"Freed {result.FreedGB:F2} GB in {result.Duration.TotalMilliseconds:F0}ms " +
                      $"({result.BeforeAvailableGB:F2} GB \u2192 {result.AfterAvailableGB:F2} GB)"
                    : $"Cleanup failed: {result.Error}";
            });
        });
    }
}