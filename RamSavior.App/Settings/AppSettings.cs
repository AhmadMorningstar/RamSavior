namespace RamSavior.App.Settings;

public enum ThemeChoice
{
    System,
    Light,
    Dark
}

public class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Hex string, e.g. "#FFB800". Defaults to the amber you already have.</summary>
    public string AccentColorHex { get; set; } = "#FFB800";

    /// <summary>
    /// Gates the "advanced" purge items (full standby purge, modified-page flush) in
    /// Custom mode. Off by default — these are the options most likely to cause a brief
    /// stutter, so a user should opt in deliberately.
    /// </summary>
    public bool EnableAdvancedCleaning { get; set; } = false;

    /// <summary>
    /// Shrinks UI spacing/scale for smaller screens. Applied via a layout scale
    /// transform so every element shrinks together rather than needing per-control
    /// overrides.
    /// </summary>
    public bool CompactMode { get; set; } = false;

    /// <summary>
    /// Gates features that use documented-but-more-targeted APIs beyond the core system
    /// purge commands (currently: per-process working set trimming). Separate from
    /// Advanced because these affect individually chosen apps rather than the whole
    /// system, and are newer/less battle-tested in this app specifically.
    /// </summary>
    public bool EnableExperimentalFeatures { get; set; } = false;

    /// <summary>
    /// By default automation only ever runs Normal or Moderate tier cleans — a deliberate
    /// safety choice, since actions with real tradeoffs shouldn't fire unattended without
    /// the user present to notice if something feels off. Custom is possible (see
    /// <see cref="AutomationTier"/>/<see cref="AutomationCustomItems"/>) but only while the
    /// main window itself is set to Advanced or Experimental mode — a user who has never
    /// opted into Advanced/Experimental cleaning by hand can't end up with it running
    /// silently in the background either.
    /// </summary>
    public RamSavior.Core.Automation.AutomationTrigger Automation { get; set; } = new();

    /// <summary>Which tier automation uses when it fires. Custom is only honored while
    /// <see cref="EnableAdvancedCleaning"/> is true; otherwise automation clamps to Normal/Moderate.</summary>
    public RamSavior.Core.Engine.CleanMode AutomationTier { get; set; } = RamSavior.Core.Engine.CleanMode.Normal;

    /// <summary>Tracks whether we've already told the user "closing minimizes to tray" once.</summary>
    public bool HasShownTrayHint { get; set; } = false;

    /// <summary>First-run onboarding — shown once, then never again unless settings are reset.</summary>
    public bool HasSeenWelcome { get; set; } = false;

    /// <summary>Backed by a Scheduled Task (see TaskSchedulerIntegration), not a registry Run key —
    /// this app requires admin, and Run-key entries don't reliably re-elevate at logon.</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>Fixed at Ctrl+Alt+R for now — no key-capture UI yet, see project notes.</summary>
    public bool GlobalHotkeyEnabled { get; set; } = false;

    /// <summary>
    /// Focus Mode's keep-list persists between launches for convenience, but the running
    /// state itself does not — Focus Mode always starts stopped, since it's meant to be a
    /// deliberate, attended session, not something that silently resumes in the background.
    /// </summary>
    public FocusModeSettings FocusMode { get; set; } = new();

    /// <summary>
    /// Which cleanup items automation is allowed to run when <see cref="AutomationTier"/> is
    /// set to Custom. Only ever honored while <see cref="EnableAdvancedCleaning"/> is true —
    /// switching the main window back to Normal mode silently falls back to a safe
    /// Normal/Moderate tier for automation, even if this list still has entries saved.
    /// </summary>
    public List<RamSavior.Core.Engine.MemoryListCommand> AutomationCustomItems { get; set; } = new();

    /// <summary>Which sections of the main window are shown. Only editable (via the Layout
    /// icon) in Experimental mode, but applies to whichever mode is active.</summary>
    public MainLayoutSettings Layout { get; set; } = new();
}

public class FocusModeSettings
{
    public List<string> KeepProcessNames { get; set; } = new();
    public int IntervalSeconds { get; set; } = 20;

    /// <summary>Height in device-independent pixels of the resizable protected-apps list.
    /// Only adjustable (via the drag handle) in Experimental mode.</summary>
    public double ListHeight { get; set; } = 180;
}

/// <summary>
/// Lets a user hide main-window sections they don't use (e.g. someone who never touches
/// Automation can reclaim that space for Focus Mode). Everything defaults to visible so
/// existing users see no change until they open the Layout picker themselves.
/// </summary>
public class MainLayoutSettings
{
    public bool ShowMemoryStatus { get; set; } = true;
    public bool ShowInsights { get; set; } = true;
    public bool ShowAutomation { get; set; } = true;
    public bool ShowClean { get; set; } = true;
    public bool ShowFocusMode { get; set; } = true;
    public bool ShowPerProcessTrim { get; set; } = true;

    /// <summary>
    /// Only meaningful in Experimental mode. False (default) = sections automatically
    /// reflow to fill whatever space is available (a WrapPanel — hiding a section simply
    /// closes the gap). True = the user has opted into freely dragging and resizing each
    /// section anywhere on a free-form canvas, with edge/alignment snapping — a docking
    /// IDE / Windows desktop feel, not a fixed grid.
    /// </summary>
    public bool UseCustomArrangement { get; set; } = false;

    /// <summary>Each section's exact position and size on the free-form canvas. A section
    /// key missing from this map gets a sensible cascading default the first time it's
    /// dragged onto the canvas.</summary>
    public Dictionary<string, PanelBounds> Positions { get; set; } = new();

    /// <summary>Named custom arrangements the user has explicitly saved via "Save Current
    /// As..." in the Layout window, so they can switch between a couple of favorite
    /// setups instead of only ever having the one currently on screen.</summary>
    public Dictionary<string, SavedArrangement> SavedArrangements { get; set; } = new();
}

/// <summary>One section's exact position and size on the free-form Experimental canvas.</summary>
public class PanelBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 280;
}

/// <summary>A snapshot of every section's position/size saved under a name via the Layout window.</summary>
public class SavedArrangement
{
    public Dictionary<string, PanelBounds> Positions { get; set; } = new();
}
