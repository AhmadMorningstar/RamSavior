using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RamSavior.App.Automation;
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
    private readonly FocusModeController _focusModeController;

    private readonly List<string> _focusPickerNames = new();
    private readonly HashSet<string> _focusSelectedNames = new(StringComparer.OrdinalIgnoreCase);

    private string _insightsTab = "Status";
    private bool _isLoaded;
    private double _lastAutoWidth;

    /// <summary>Populated fresh by InitSectionMap() every time the visual tree is (re)built —
    /// after a theme-triggered ReloadUi() the old elements are gone, so this must never be
    /// captured once and assumed to stay valid.</summary>
    private Dictionary<string, FrameworkElement> _sections = new();
    private List<Border> _slotBorders = new();
    private static readonly string[] SectionOrder =
        { "MemoryStatus", "Insights", "Automation", "Clean", "FocusMode", "PerProcessTrim" };

    // Per-mode sizing so switching modes doesn't leave the window too small (text/buttons
    // crowded) or oddly oversized. MinWidth/MinHeight are hard floors; PreferredWidth is
    // only applied when the window still looks like it's at its previous mode's auto size
    // (i.e. the user hasn't manually resized it) — see ApplyModeSizing.
    private static readonly (double MinW, double MinH, double PreferredW) NormalSize = (480, 480, 900);
    private static readonly (double MinW, double MinH, double PreferredW) AdvancedSize = (480, 560, 1000);
    private static readonly (double MinW, double MinH, double PreferredW) ExperimentalSize = (480, 620, 1400);

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _focusModeController = new FocusModeController(_settings);
        _focusModeController.TickCompleted += result => Dispatcher.Invoke(() => OnFocusModeTick(result));

        InitializeContent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshStatus();
        _pollTimer.Start();

        _leakScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _leakScanTimer.Tick += (_, _) => RunLeakScan();
        _leakScanTimer.Start();

        RefreshStatus();
    }

    /// <summary>
    /// Everything needed to populate a freshly-parsed visual tree: building dynamic
    /// panels, wiring the mode/layout/arrangement state, and restoring anything that
    /// isn't itself part of the static XAML (Focus Mode's running state, current theme
    /// icon, etc). Split out from the constructor specifically so ReloadUi() can re-run
    /// it against a brand new tree without duplicating any of this.
    /// </summary>
    private void InitializeContent()
    {
        InitSectionMap();

        BuildCustomItemsPanel();
        ApplyCompactMode();
        FocusListRow.Height = new GridLength(Math.Clamp(_settings.FocusMode.ListHeight, 80, 500));
        UpdateThemeToggleIcon();

        _isLoaded = false;
        UiModeComboBox.SelectedIndex = _settings.EnableExperimentalFeatures ? 2 : _settings.EnableAdvancedCleaning ? 1 : 0;
        ApplyUiMode();
        RefreshAutomationSummary();
        RefreshInsights();
        _isLoaded = true;

        if (_focusModeController.IsRunning)
        {
            FocusModeToggleButton.Content = "Stop Focus Mode";
            FocusModeToggleButton.Appearance = ControlAppearance.Danger;
            FocusModeStatusText.Text = $"Focus Mode running \u2014 protecting: {string.Join(", ", _focusSelectedNames)}. Trimming everything else every {_settings.FocusMode.IntervalSeconds}s.";
        }
    }

    private void InitSectionMap()
    {
        _sections = new Dictionary<string, FrameworkElement>
        {
            ["MemoryStatus"] = MemoryStatusSection,
            ["Insights"] = InsightsSection,
            ["Automation"] = AutomationSection,
            ["Clean"] = CleanSection,
            ["FocusMode"] = FocusModeSection,
            ["PerProcessTrim"] = PerProcessTrimSection,
        };
        _slotBorders = new List<Border> { Slot0, Slot1, Slot2, Slot3, Slot4, Slot5 };
    }

    /// <summary>
    /// Rebuilds the entire visual tree in place by re-running InitializeComponent and all
    /// UI-populating setup. WPF-UI has a known open issue (lepoco/wpfui#1481) where some
    /// controls don't fully repaint on a live theme switch — chasing every affected
    /// control individually is fragile, so this instead throws the whole tree away and
    /// reconstructs it fresh against the now-current theme resources, which is guaranteed
    /// correct because it's exactly what happens whenever any new window opens (which is
    /// why re-opening Settings always "fixed" it). The Window object itself keeps its
    /// identity — App's reference to it, the tray hooks, the global hotkey target, and the
    /// Closing-to-tray handler are all untouched, since only the content is rebuilt, not
    /// the window. Current position/size/state are captured and restored around the
    /// rebuild so this is invisible to the user beyond the repaint itself.
    /// </summary>
    internal void ReloadUi()
    {
        double left = Left, top = Top, width = Width, height = Height;
        var state = WindowState;

        InitializeComponent();
        InitializeContent();

        if (state == WindowState.Normal)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        WindowState = state;

        RefreshStatus();
    }

    // ----- Called from App.xaml.cs / tray -----

    public void TriggerQuickClean() => Dispatcher.Invoke(() => CleanButton_Click(this, new RoutedEventArgs()));

    public void NotifyAutomationRanInBackground(CleanupResult result)
    {
        RefreshStatus();
        RefreshInsights();
        ResultText.Text = result.Success
            ? $"Automation freed {result.FreedGB:F2} GB in the background just now."
            : $"Automation attempt failed: {result.Error}";
    }

    // ----- Sizing: auto-fit ONCE on first show, then the user owns the window size.
    // Previously this also ran on every Mode/Compact change, which is exactly what caused
    // the "shrinks to half the screen" bug — forcing SizeToContent on a maximized window
    // knocks it out of the maximized state. Now it never runs again after first launch. -----

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (WindowState != WindowState.Normal) return; // don't fight a maximized/minimized start state

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

    // ----- Top-level Mode selector -----

    private void UiModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        _settings.EnableAdvancedCleaning = UiModeComboBox.SelectedIndex >= 1;
        _settings.EnableExperimentalFeatures = UiModeComboBox.SelectedIndex >= 2;
        SettingsStore.Save(_settings);

        ApplyUiMode();
        // Deliberately NOT resizing the window here — see the note on Window_Loaded above.
    }

    private void ApplyUiMode()
    {
        int level = UiModeComboBox.SelectedIndex; // 0 Normal, 1 Advanced, 2 Experimental

        ModeDescriptionText.Text = level switch
        {
            1 => "Full manual control over exactly what gets cleaned.",
            2 => "Advanced, plus per-process tools and a custom drag-to-arrange layout.",
            _ => "Safe, automation-friendly cleaning using known Windows APIs."
        };

        CustomTierOption.Visibility = level >= 1 ? Visibility.Visible : Visibility.Collapsed;
        LayoutButton.Visibility = level >= 2 ? Visibility.Visible : Visibility.Collapsed;

        if (level < 1 && CustomModeRadio.IsChecked == true)
            NormalModeRadio.IsChecked = true;

        ApplyAdvancedCheckboxAvailability();
        ApplyLayoutVisibility();
        ApplyArrangementMode();
        ApplyModeSizing(level);

        if (level >= 2 && ProcessComboBox.Items.Count == 0)
            RefreshProcessList();
        if (level >= 2 && _focusPickerNames.Count == 0)
            PopulateFocusList();
    }

    /// <summary>
    /// Grows the window (and raises its floor) to fit whichever mode is now active,
    /// instead of leaving the user to drag it wider/narrower by hand every time they
    /// switch modes — that manual-resize dance was the original complaint. Only ever
    /// grows automatically; if the user has deliberately made the window wider than the
    /// mode's preferred size, that choice is left alone.
    /// </summary>
    private void ApplyModeSizing(int level)
    {
        var target = level switch
        {
            2 => ExperimentalSize,
            1 => AdvancedSize,
            _ => NormalSize
        };

        MinWidth = target.MinW;
        MinHeight = target.MinH;

        if (WindowState != WindowState.Normal) return; // don't fight a maximized/minimized window

        bool looksAutoSized = !_isLoaded || Width <= _lastAutoWidth + 0.5;

        if (looksAutoSized || Width < target.MinW)
        {
            Width = Math.Max(target.PreferredW, target.MinW);
            _lastAutoWidth = Width;
        }

        if (Height < target.MinH)
            Height = Math.Min(target.MinH, SystemParameters.WorkArea.Height - 40);
    }

    // ----- Quick light/dark appearance toggle -----

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Deliberately resolves to a concrete Light/Dark choice rather than toggling
        // "System" — once someone reaches for a manual quick-switch they want a definite
        // answer, and the icon needs to reliably reflect the app's actual appearance.
        _settings.Theme = IsEffectivelyLight() ? ThemeChoice.Dark : ThemeChoice.Light;
        SettingsStore.Save(_settings);
        ThemeApplier.Apply(_settings);
        ReloadUi();
    }

    private bool IsEffectivelyLight() => _settings.Theme switch
    {
        ThemeChoice.Light => true,
        ThemeChoice.Dark => false,
        _ => ThemeApplier.IsWindowsUsingLightTheme()
    };

    private void UpdateThemeToggleIcon()
    {
        bool light = IsEffectivelyLight();
        ThemeToggleButton.Icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = light ? Wpf.Ui.Controls.SymbolRegular.WeatherSunny24 : Wpf.Ui.Controls.SymbolRegular.WeatherMoon24
        };
        ThemeToggleButton.ToolTip = light ? "Switch to dark appearance" : "Switch to light appearance";
    }

    // ----- Layout picker (Experimental only): visibility + arrangement (auto vs custom) -----

    private void LayoutButton_Click(object sender, RoutedEventArgs e)
    {
        var layoutWindow = new LayoutWindow(_settings) { Owner = this };
        layoutWindow.LayoutChanged = () =>
        {
            ApplyLayoutVisibility();
            ApplyArrangementMode();
        };
        layoutWindow.ShowDialog();
    }

    private void ApplyLayoutVisibility()
    {
        var layout = _settings.Layout;
        bool experimental = UiModeComboBox.SelectedIndex >= 2;

        MemoryStatusSection.Visibility = layout.ShowMemoryStatus ? Visibility.Visible : Visibility.Collapsed;
        InsightsSection.Visibility = layout.ShowInsights ? Visibility.Visible : Visibility.Collapsed;
        AutomationSection.Visibility = layout.ShowAutomation ? Visibility.Visible : Visibility.Collapsed;
        CleanSection.Visibility = layout.ShowClean ? Visibility.Visible : Visibility.Collapsed;
        // Focus Mode and Per-Process Trim are Experimental-only tools regardless of the
        // Layout checkbox state — the checkbox only matters once already in that mode.
        FocusModeSection.Visibility = experimental && layout.ShowFocusMode ? Visibility.Visible : Visibility.Collapsed;
        PerProcessTrimSection.Visibility = experimental && layout.ShowPerProcessTrim ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Switches the body between the automatic reflowing WrapPanel (sections just close
    /// ranks around whatever's hidden — no manual arranging needed) and the Experimental
    /// hand-arranged 3x2 slot grid. Reparents the actual section elements between
    /// whichever container is now active; nothing is duplicated.
    /// </summary>
    private void ApplyArrangementMode()
    {
        bool custom = _settings.Layout.UseCustomArrangement && UiModeComboBox.SelectedIndex >= 2;

        FlowLayoutPanel.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        CustomLayoutGrid.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

        if (custom)
        {
            ApplyColumnRowWeights();
            MoveSectionsIntoSlots();
        }
        else
        {
            MoveSectionsIntoFlow();
        }
    }

    private void MoveSectionsIntoFlow()
    {
        foreach (var key in SectionOrder)
        {
            var el = _sections[key];
            if (!ReferenceEquals(el.Parent, FlowLayoutPanel))
            {
                RemoveFromCurrentParent(el);
                FlowLayoutPanel.Children.Add(el);
            }
        }
    }

    private void MoveSectionsIntoSlots()
    {
        var assignment = _settings.Layout.SlotAssignment;

        // Validate existing assignments (in range, no duplicates), then fill in a slot for
        // any section that doesn't have one yet.
        var used = new HashSet<int>();
        foreach (var key in SectionOrder.ToList())
        {
            if (assignment.TryGetValue(key, out int slot) && slot is >= 0 and < 6 && used.Add(slot))
                continue;
            assignment.Remove(key);
        }
        int nextFree = 0;
        foreach (var key in SectionOrder)
        {
            if (assignment.ContainsKey(key)) continue;
            while (used.Contains(nextFree)) nextFree++;
            assignment[key] = nextFree;
            used.Add(nextFree);
        }

        foreach (var slot in _slotBorders)
            slot.Child = null;

        foreach (var key in SectionOrder)
        {
            RemoveFromCurrentParent(_sections[key]);
            _slotBorders[assignment[key]].Child = _sections[key];
        }
    }

    private static void RemoveFromCurrentParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case System.Windows.Controls.Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
        }
    }

    private void ApplyColumnRowWeights()
    {
        var cw = _settings.Layout.ColumnWeights.Length == 3 ? _settings.Layout.ColumnWeights : new double[] { 1, 1, 1 };
        var rw = _settings.Layout.RowWeights.Length == 2 ? _settings.Layout.RowWeights : new double[] { 1, 1 };

        SlotCol0.Width = new GridLength(Math.Max(cw[0], 0.1), GridUnitType.Star);
        SlotCol1.Width = new GridLength(Math.Max(cw[1], 0.1), GridUnitType.Star);
        SlotCol2.Width = new GridLength(Math.Max(cw[2], 0.1), GridUnitType.Star);
        SlotRow0.Height = new GridLength(Math.Max(rw[0], 0.1), GridUnitType.Star);
        SlotRow1.Height = new GridLength(Math.Max(rw[1], 0.1), GridUnitType.Star);
    }

    private void SlotSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.Layout.ColumnWeights = new[] { SlotCol0.Width.Value, SlotCol1.Width.Value, SlotCol2.Width.Value };
        _settings.Layout.RowWeights = new[] { SlotRow0.Height.Value, SlotRow1.Height.Value };
        SettingsStore.Save(_settings);
    }

    // ----- Custom arrangement drag-and-drop: drag a section header, drop it on a slot -----

    private void SectionHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_settings.Layout.UseCustomArrangement || UiModeComboBox.SelectedIndex < 2) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string key) return;

        DragDrop.DoDragDrop(fe, key, System.Windows.DragDropEffects.Move);
    }

    private void Slot_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border slot && e.Data.GetDataPresent(typeof(string)))
            slot.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    private void Slot_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border slot) slot.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void Slot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Slot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border targetSlot) return;
        targetSlot.Background = System.Windows.Media.Brushes.Transparent;

        if (!e.Data.GetDataPresent(typeof(string))) return;
        string draggedKey = (string)e.Data.GetData(typeof(string))!;

        int targetIndex = _slotBorders.IndexOf(targetSlot);
        var assignment = _settings.Layout.SlotAssignment;
        if (targetIndex < 0 || !assignment.TryGetValue(draggedKey, out int sourceIndex) || sourceIndex == targetIndex)
            return;

        string? occupantKey = assignment.FirstOrDefault(kv => kv.Value == targetIndex && kv.Key != draggedKey).Key;

        assignment[draggedKey] = targetIndex;
        if (occupantKey != null) assignment[occupantKey] = sourceIndex;

        SettingsStore.Save(_settings);
        MoveSectionsIntoSlots();
    }

    // ----- Focus Mode list resize handle (Experimental) -----

    private void FocusListResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double current = FocusListRow.Height.Value;
        FocusListRow.Height = new GridLength(Math.Clamp(current + e.VerticalChange, 80, 500));
    }

    private void FocusListResizeThumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.FocusMode.ListHeight = FocusListRow.Height.Value;
        SettingsStore.Save(_settings);
    }

    // ----- Insights (Status / History / Last Cleaned) -----

    private void InsightsTab_Click(object sender, RoutedEventArgs e)
    {
        _insightsTab = sender switch
        {
            _ when ReferenceEquals(sender, InsightsHistoryTabButton) => "History",
            _ when ReferenceEquals(sender, InsightsLastCleanedTabButton) => "LastCleaned",
            _ => "Status"
        };

        InsightsStatusTabButton.Appearance = _insightsTab == "Status" ? ControlAppearance.Primary : ControlAppearance.Secondary;
        InsightsHistoryTabButton.Appearance = _insightsTab == "History" ? ControlAppearance.Primary : ControlAppearance.Secondary;
        InsightsLastCleanedTabButton.Appearance = _insightsTab == "LastCleaned" ? ControlAppearance.Primary : ControlAppearance.Secondary;

        RefreshInsights();
    }

    private void RefreshInsights()
    {
        InsightsContentPanel.Children.Clear();

        switch (_insightsTab)
        {
            case "History":
                var recent = JsonLogger.ReadRecent(SettingsStore.HistoryLogPath, 5);
                if (recent.Count == 0)
                {
                    AddInsightsLine("No cleaning history yet.", 0.6);
                    break;
                }
                foreach (var entry in recent)
                {
                    string time = DateTimeOffset.TryParse(entry.Timestamp, out var dto) ? dto.ToLocalTime().ToString("MMM d, h:mm tt") : entry.Timestamp;
                    AddInsightsLine($"{time} \u2014 {entry.Mode} ({entry.Source})", 0.9, bold: true);
                    AddInsightsLine(entry.Success ? $"Freed {entry.FreedGB:F2} GB" : $"Failed: {entry.Error}", 0.6);
                }
                break;

            case "LastCleaned":
                var last = JsonLogger.ReadRecent(SettingsStore.HistoryLogPath, 1).FirstOrDefault();
                if (last is null)
                {
                    AddInsightsLine("Nothing cleaned yet this session or before.", 0.6);
                    break;
                }
                string lastTime = DateTimeOffset.TryParse(last.Timestamp, out var ldto) ? ldto.ToLocalTime().ToString("MMM d, h:mm tt") : last.Timestamp;
                AddInsightsLine(lastTime, 0.6);
                AddInsightsLine(last.Success ? $"{last.Mode} \u2014 freed {last.FreedGB:F2} GB" : $"{last.Mode} \u2014 failed", 0.9, bold: true, big: true);
                AddInsightsLine(last.Success ? $"{last.BeforeAvailableGB:F2} GB \u2192 {last.AfterAvailableGB:F2} GB \u2022 {last.DurationMs:F0}ms \u2022 via {last.Source}" : last.Error ?? "", 0.6);
                break;

            default:
                var top = ProcessTrimmer.GetTopProcessesByMemory(3);
                AddInsightsLine("Top memory users right now:", 0.6);
                foreach (var proc in top)
                    AddInsightsLine($"{proc.Name} \u2014 {proc.WorkingSetMB:F0} MB", 0.85);
                break;
        }
    }

    private void AddInsightsLine(string text, double opacity, bool bold = false, bool big = false)
    {
        InsightsContentPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = text,
            Opacity = opacity,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = big ? 16 : 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
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

        AddSegment(active, System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));
        AddSegment(standby, System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        AddSegment(modified, System.Windows.Media.Color.FromRgb(0xE5, 0x7A, 0x1A));
        AddSegment(free, System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
        AddSegment(zeroed, System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));

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

        if (UiModeComboBox.SelectedIndex < 2) return;

        var suspects = _leakTracker.GetSuspects(current);
        LeakSuspectsText.Text = suspects.Count == 0
            ? ""
            : "Climbing steadily: " + string.Join(", ", suspects.Select(s => $"{s.Name} (+{s.GrowthPercent:F0}%, {s.CurrentMB:F0} MB)")) +
              ". Not a diagnosis — just worth a look.";
    }

    // ----- Focus Mode (Experimental) -----

    private void PopulateFocusList()
    {
        _focusPickerNames.Clear();

        foreach (var proc in ProcessTrimmer.GetAllProcessesSortedByName())
            _focusPickerNames.Add(proc.Name);

        foreach (var saved in _settings.FocusMode.KeepProcessNames)
        {
            _focusSelectedNames.Add(saved);
            if (!_focusPickerNames.Contains(saved, StringComparer.OrdinalIgnoreCase))
                _focusPickerNames.Add(saved);
        }

        RenderFocusList();
    }

    private void RenderFocusList()
    {
        string filter = FocusSearchBox.Text?.Trim() ?? "";

        FocusKeepPanel.Children.Clear();

        foreach (var name in _focusPickerNames
                     .Where(n => filter.Length == 0 || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            FocusKeepPanel.Children.Add(BuildFocusRow(name));
        }
    }

    /// <summary>
    /// Right-click selects, left-click deselects — inverted from the usual convention on
    /// purpose, per how this feature is meant to be used: right-click to quickly protect
    /// several apps in a row without needing Ctrl held down, left-click to back one out.
    /// Built as a plain Border (not a CheckBox/ListBoxItem) specifically so nothing here
    /// has its own default left-click behavior we'd need to fight.
    /// </summary>
    private Border BuildFocusRow(string name)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = label
        };

        void ApplySelectedVisual(bool selected)
        {
            row.Background = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 0xFF, 0xB8, 0x00))
                : System.Windows.Media.Brushes.Transparent;
            label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        ApplySelectedVisual(_focusSelectedNames.Contains(name));

        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _focusSelectedNames.Remove(name);
            ApplySelectedVisual(false);
        };

        row.PreviewMouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            _focusSelectedNames.Add(name);
            ApplySelectedVisual(true);
        };

        return row;
    }

    private void FocusSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderFocusList();

    private void RefreshFocusListButton_Click(object sender, RoutedEventArgs e) => PopulateFocusList();

    private void DeselectAllFocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusSelectedNames.Count == 0)
        {
            FocusModeStatusText.Text = "Nothing is protected yet.";
            return;
        }

        _focusSelectedNames.Clear();
        RenderFocusList();
        FocusModeStatusText.Text = "Cleared — no apps are protected. Pick at least one before starting Focus Mode.";
    }

    private void AddFocusFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder — every .exe directly inside it will be protected."
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        string[] exeFiles;
        try
        {
            exeFiles = System.IO.Directory.GetFiles(dialog.SelectedPath, "*.exe", System.IO.SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            FocusModeStatusText.Text = $"Couldn't read that folder: {ex.Message}";
            return;
        }

        foreach (var file in exeFiles)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (!_focusPickerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                _focusPickerNames.Add(name);
            _focusSelectedNames.Add(name);
        }

        RenderFocusList();
        FocusModeStatusText.Text = exeFiles.Length == 0
            ? "No .exe files found directly in that folder."
            : $"Added {exeFiles.Length} app(s) from folder.";
    }

    private void AddFocusFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            Title = "Choose an application to protect"
        };

        if (dialog.ShowDialog() != true) return;

        string name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        if (!_focusPickerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            _focusPickerNames.Add(name);
        _focusSelectedNames.Add(name);

        RenderFocusList();
        FocusModeStatusText.Text = $"Added {name}.";
    }

    private void FocusModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusModeController.IsRunning)
        {
            _focusModeController.Stop();
            FocusModeToggleButton.Content = "Start Focus Mode";
            FocusModeToggleButton.Appearance = ControlAppearance.Primary;
            FocusModeStatusText.Text = "Focus Mode stopped.";
            return;
        }

        if (_focusSelectedNames.Count == 0)
        {
            FocusModeStatusText.Text = "Select at least one app to protect first.";
            return;
        }

        _settings.FocusMode.KeepProcessNames = _focusSelectedNames.ToList();
        SettingsStore.Save(_settings);

        _focusModeController.Start();
        FocusModeToggleButton.Content = "Stop Focus Mode";
        FocusModeToggleButton.Appearance = ControlAppearance.Danger;
        FocusModeStatusText.Text = $"Focus Mode running \u2014 protecting: {string.Join(", ", _focusSelectedNames)}. Trimming everything else every {_settings.FocusMode.IntervalSeconds}s.";
    }

    private void OnFocusModeTick(RamSavior.Core.Automation.FocusModeTickResult result)
    {
        RefreshStatus();
        RefreshInsights();
        FocusModeStatusText.Text =
            $"Trimmed {result.ProcessesTrimmed} other process(es), freed {result.SystemCleanResult.FreedGB:F2} GB system-wide. " +
            $"Still protecting: {string.Join(", ", _focusSelectedNames)}.";
    }

    // ----- Automation quick card -----

    private void RefreshAutomationSummary()
    {
        AutomationQuickToggle.IsChecked = _settings.Automation.Enabled;

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

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var historyWindow = new HistoryWindow(SettingsStore.HistoryLogPath) { Owner = this };
        historyWindow.ShowDialog();
        RefreshInsights();
    }

    // ----- Advanced checkbox availability -----

    private void ApplyAdvancedCheckboxAvailability()
    {
        foreach (var info in MemoryCommandCatalog.All)
        {
            if (!info.IsAdvanced) continue;

            var checkBox = _customCheckboxes[info.Command];
            var container = _customRowContainers[info.Command];

            checkBox.IsEnabled = _settings.EnableAdvancedCleaning;
            if (!_settings.EnableAdvancedCleaning) checkBox.IsChecked = false;

            container.Opacity = _settings.EnableAdvancedCleaning ? 1.0 : 0.5;
        }
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

    private void CleanTierRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomPanel is null) return;
        CustomPanel.Visibility = CustomModeRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        WireSettingsCallbacks(settingsWindow);
        settingsWindow.ShowDialog();
    }

    private void AutomationConfigButton_Click(object sender, RoutedEventArgs e) => OpenAutomationConfig();

    private void ConfigureAutomationButton_Click(object sender, RoutedEventArgs e) => OpenAutomationConfig();

    private void OpenAutomationConfig()
    {
        var automationWindow = new AutomationConfigWindow(_settings) { Owner = this };
        automationWindow.AutomationChanged = () =>
        {
            RefreshAutomationSummary();
            (System.Windows.Application.Current as App)?.RestartAutomationIfNeeded();
        };
        automationWindow.ShowDialog();
        RefreshAutomationSummary();
    }

    private void WireSettingsCallbacks(SettingsWindow settingsWindow)
    {
        settingsWindow.CompactModeChanged = () => ApplyCompactMode();
        settingsWindow.ThemeOrAccentChanged = () => ReloadUi();
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

        if (_insightsTab == "Status") RefreshInsights();
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        bool isCustomTier = CustomModeRadio.IsChecked == true;
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
                RefreshInsights();

                ResultText.Text = result.Success
                    ? $"Freed {result.FreedGB:F2} GB in {result.Duration.TotalMilliseconds:F0}ms " +
                      $"({result.BeforeAvailableGB:F2} GB \u2192 {result.AfterAvailableGB:F2} GB)"
                    : $"Cleanup failed: {result.Error}";
            });
        });
    }
}
