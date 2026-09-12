using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RamSavior.App.Settings;
using RamSavior.Core.Engine;
using RamSavior.Core.Logging;
using RamSavior.Core.Monitoring;
using RamSavior.Core.ProcessTrim;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _leakScanTimer;
    private readonly AppSettings _settings;
    private readonly Dictionary<MemoryListCommand, System.Windows.Controls.CheckBox> _customCheckboxes = new();
    private readonly Dictionary<MemoryListCommand, StackPanel> _customRowContainers = new();
    private readonly ProcessMemoryTracker _leakTracker = new();
    private bool _isLoaded;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        BuildCustomItemsPanel();
        ApplyCompactMode();
        ApplyTierGating();
        RefreshAutomationSummary();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshStatus();
        _pollTimer.Start();

        // Leak-trend sampling runs much slower than the status poll — it needs several
        // samples over minutes to mean anything, and scanning every running process is
        // heavier than reading two memory counters.
        _leakScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _leakScanTimer.Tick += (_, _) => RunLeakScan();
        _leakScanTimer.Start();

        RefreshStatus();
        _isLoaded = true;
    }

    // ----- Called from App.xaml.cs / tray -----

    public void TriggerQuickClean() => Dispatcher.Invoke(() => CleanButton_Click(this, new RoutedEventArgs()));

    public void NotifyAutomationRanInBackground(CleanupResult result)
    {
        RefreshStatus();
        ResultText.Text = result.Success
            ? $"Automation freed {result.FreedGB:F2} GB in the background just now."
            : $"Automation attempt failed: {result.Error}";
    }

    // ----- Sizing -----

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.Height;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SizeToContent = SizeToContent.Manual;
            Height = Math.Min(ActualHeight, SystemParameters.WorkArea.Height - 40);
        }), DispatcherPriority.ContextIdle);
    }

    private void ReflowWindowSize()
    {
        SizeToContent = SizeToContent.Height;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SizeToContent = SizeToContent.Manual;
            Height = Math.Min(ActualHeight, SystemParameters.WorkArea.Height - 40);
        }), DispatcherPriority.ContextIdle);
    }

    private void ApplyCompactMode()
    {
        double scale = _settings.CompactMode ? 0.85 : 1.0;
        RootContent.LayoutTransform = new ScaleTransform(scale, scale);
    }

    // ----- Memory composition bar -----

    private void RefreshCompositionBar(MemoryReading reading)
    {
        var composition = MemoryCompositionReader.TryRead();
        CompositionBar.ColumnDefinitions.Clear();
        CompositionBar.Children.Clear();

        if (composition is null)
        {
            CompositionLegendText.Text = "Detailed composition unavailable on this system.";
            return;
        }

        double totalMB = reading.TotalPhysicalGB * 1024.0;
        double standby = composition.Value.StandbyTotalMB;
        double modified = composition.Value.ModifiedMB;
        double free = composition.Value.FreeMB;
        double zeroed = composition.Value.ZeroedMB;
        double active = Math.Max(1, totalMB - standby - modified - free - zeroed);

        AddSegment(active, System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));   // Active — in use by processes
        AddSegment(standby, System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));  // Standby — reclaimable cache
        AddSegment(modified, System.Windows.Media.Color.FromRgb(0xE5, 0x7A, 0x1A)); // Modified — dirty, pending write
        AddSegment(free, System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));     // Free — immediately usable
        AddSegment(zeroed, System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));   // Zeroed — pre-zeroed free pages

        CompositionLegendText.Text =
            $"Active {active:F0} MB \u2022 Standby {standby:F0} MB \u2022 Modified {modified:F0} MB \u2022 " +
            $"Free {free:F0} MB \u2022 Zeroed {zeroed:F0} MB";
    }

    private void AddSegment(double valueMB, System.Windows.Media.Color color)
    {
        var col = new ColumnDefinition { Width = new GridLength(Math.Max(valueMB, 0.01), GridUnitType.Star) };
        CompositionBar.ColumnDefinitions.Add(col);

        var border = new Border { Background = new SolidColorBrush(color) };
        Grid.SetColumn(border, CompositionBar.ColumnDefinitions.Count - 1);
        CompositionBar.Children.Add(border);
    }

    // ----- Leak-trend scan (Experimental) -----

    private void RunLeakScan()
    {
        var current = ProcessTrimmer.GetTopProcessesByMemory(count: 15);
        _leakTracker.RecordSample(current);

        if (ExperimentalModeRadio.IsChecked != true) return;

        var suspects = _leakTracker.GetSuspects(current);
        LeakSuspectsText.Text = suspects.Count == 0
            ? ""
            : "Climbing steadily: " + string.Join(", ", suspects.Select(s => $"{s.Name} (+{s.GrowthPercent:F0}%, {s.CurrentMB:F0} MB)")) +
              ". Not a diagnosis — just worth a look.";
    }

    // ----- Automation quick card -----

    private void RefreshAutomationSummary()
    {
        AutomationQuickToggle.IsChecked = _settings.Automation.Enabled;
        AutomationStatusText.Text = _settings.Automation.Enabled ? "On" : "Off";

        var parts = new List<string>();
        if (_settings.Automation.IntervalEnabled)
            parts.Add($"every {_settings.Automation.IntervalMinutes} min");
        if (_settings.Automation.FreeMemoryThresholdEnabled)
            parts.Add($"when free RAM drops below {_settings.Automation.FreeMemoryBelowGB:F1} GB");
        if (_settings.Automation.LoadPercentThresholdEnabled)
            parts.Add($"when load exceeds {_settings.Automation.LoadAbovePercent}%");
        if (_settings.Automation.StandbyListThresholdEnabled)
            parts.Add($"when standby cache exceeds {_settings.Automation.StandbyListAboveMB:F0} MB");
        if (_settings.Automation.TimeOfDayEnabled)
            parts.Add($"daily at {_settings.Automation.TimeOfDay:hh\\:mm}");
        if (_settings.Automation.RequireIdleMinutes > 0)
            parts.Add($"only while idle {_settings.Automation.RequireIdleMinutes}+ min");

        AutomationSummaryText.Text = _settings.Automation.Enabled
            ? (parts.Count > 0 ? $"Runs {_settings.AutomationTier}: {string.Join(", ", parts)}." : "Enabled, but no trigger conditions are set — configure below.")
            : "Off. RAM Savior will only clean when you ask it to.";
    }

    private void AutomationQuickToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.Automation.Enabled = AutomationQuickToggle.IsChecked == true;
        SettingsStore.Save(_settings);
        RefreshAutomationSummary();
        (System.Windows.Application.Current as App)?.RestartAutomationIfNeeded();
    }

    private void ConfigureAutomationButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        WireSettingsCallbacks(settingsWindow);
        settingsWindow.ShowDialog();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var historyWindow = new HistoryWindow(SettingsStore.HistoryLogPath) { Owner = this };
        historyWindow.ShowDialog();
    }

    // ----- Tier gating (Advanced/Experimental require opt-in from Settings) -----

    private void ApplyTierGating()
    {
        AdvancedModeRadio.IsEnabled = _settings.EnableAdvancedCleaning;
        AdvancedModeRadio.ToolTip = _settings.EnableAdvancedCleaning ? null : "Enable Advanced Cleaning in Settings to use this tier.";

        ExperimentalModeRadio.IsEnabled = _settings.EnableExperimentalFeatures;
        ExperimentalModeRadio.ToolTip = _settings.EnableExperimentalFeatures ? null : "Enable Experimental Features in Settings to use this tier.";

        if (AdvancedModeRadio.IsChecked == true && !_settings.EnableAdvancedCleaning) NormalModeRadio.IsChecked = true;
        if (ExperimentalModeRadio.IsChecked == true && !_settings.EnableExperimentalFeatures) NormalModeRadio.IsChecked = true;

        ApplyAdvancedGating();
        ApplyExperimentalVisibility();
    }

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

    private void ApplyExperimentalVisibility()
    {
        bool show = _settings.EnableExperimentalFeatures && ExperimentalModeRadio.IsChecked == true;
        ExperimentalSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (show && ProcessComboBox.Items.Count == 0)
            RefreshProcessList();
    }

    private void RefreshProcessList()
    {
        ProcessComboBox.Items.Clear();

        foreach (var proc in ProcessTrimmer.GetTopProcessesByMemory())
        {
            ProcessComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = $"{proc.Name} (PID {proc.Pid}) \u2014 {proc.WorkingSetMB:F1} MB",
                Tag = proc.Pid
            });
        }

        if (ProcessComboBox.Items.Count > 0)
            ProcessComboBox.SelectedIndex = 0;
    }

    private void RefreshProcessesButton_Click(object sender, RoutedEventArgs e) => RefreshProcessList();

    private void TrimProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item || item.Tag is not int pid)
        {
            ExperimentalResultText.Text = "Pick a process first.";
            return;
        }

        TrimProcessButton.IsEnabled = false;
        ExperimentalResultText.Text = "Trimming...";

        Task.Run(() => ProcessTrimmer.TrimProcess(pid)).ContinueWith(t =>
        {
            var (success, error) = t.Result;

            Dispatcher.Invoke(() =>
            {
                TrimProcessButton.IsEnabled = true;
                ExperimentalResultText.Text = success ? "Trimmed successfully." : $"Failed: {error}";
            });
        });
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

            var checkBox = new System.Windows.Controls.CheckBox { Content = info.Title, FontWeight = FontWeights.SemiBold };
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
            CustomItemsPanel.Children.Add(container);

            _customCheckboxes[info.Command] = checkBox;
            _customRowContainers[info.Command] = container;
        }
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomPanel is null) return;

        bool isCustomStyleTier = AdvancedModeRadio.IsChecked == true || ExperimentalModeRadio.IsChecked == true;
        CustomPanel.Visibility = isCustomStyleTier ? Visibility.Visible : Visibility.Collapsed;

        ApplyExperimentalVisibility();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        WireSettingsCallbacks(settingsWindow);
        settingsWindow.ShowDialog();
    }

    private void WireSettingsCallbacks(SettingsWindow settingsWindow)
    {
        settingsWindow.AdvancedCleaningChanged = () => { ApplyTierGating(); };
        settingsWindow.CompactModeChanged = () => { ApplyCompactMode(); ReflowWindowSize(); };
        settingsWindow.ExperimentalFeaturesChanged = () => { ApplyTierGating(); ReflowWindowSize(); };
        settingsWindow.AutomationChanged = () =>
        {
            RefreshAutomationSummary();
            (System.Windows.Application.Current as App)?.RestartAutomationIfNeeded();
        };
        settingsWindow.StartWithWindowsChanged = () =>
            (System.Windows.Application.Current as App)?.RefreshStartWithWindowsTask();
        settingsWindow.GlobalHotkeyChanged = () =>
            (System.Windows.Application.Current as App)?.RefreshHotKey();
    }

    private void RefreshStatus()
    {
        var reading = MemoryStatus.Read();

        AvailableText.Text = $"{reading.AvailablePhysicalGB:F2} GB available";
        TotalText.Text = $"of {reading.TotalPhysicalGB:F2} GB total";
        LoadBar.Value = reading.MemoryLoadPercent;
        LoadPercentText.Text = $"{reading.MemoryLoadPercent}% memory load";

        RefreshCompositionBar(reading);
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        bool isCustomTier = AdvancedModeRadio.IsChecked == true || ExperimentalModeRadio.IsChecked == true;
        System.Collections.Generic.HashSet<MemoryListCommand>? selected = null;

        if (isCustomTier)
        {
            selected = _customCheckboxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToHashSet();

            if (selected.Count == 0)
            {
                ResultText.Text = "Select at least one item to clean.";
                return;
            }
        }

        var mode = ModerateModeRadio.IsChecked == true ? CleanMode.Moderate : CleanMode.Normal;

        if (DryRunCheckBox.IsChecked == true)
        {
            var preview = isCustomTier ? CleanupEngine.PreviewCustom(selected!) : CleanupEngine.Preview(mode);

            string items = preview.WouldRun.Count == 0
                ? "nothing selected"
                : string.Join(", ", preview.WouldRun.Select(i => i.Title));

            ResultText.Text = $"Dry run — would run: {items}. " +
                $"Currently {preview.CurrentAvailableGB:F2} GB available" +
                (preview.CurrentStandbyMB >= 0 ? $", {preview.CurrentStandbyMB:F0} MB in standby" : "") + ". " +
                (preview.PrivilegesAvailable ? "Privileges OK." : $"Privilege check failed: {preview.PrivilegeNote}");
            return;
        }

        Func<CleanupResult> runAction = isCustomTier
            ? () => CleanupEngine.RunCustom(selected!)
            : () => CleanupEngine.Run(mode);

        CleanButton.IsEnabled = false;
        ResultText.Text = "Cleaning...";

        Task.Run(runAction).ContinueWith(t =>
        {
            var result = t.Result;
            JsonLogger.Append(SettingsStore.HistoryLogPath, result, source: "manual");

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
