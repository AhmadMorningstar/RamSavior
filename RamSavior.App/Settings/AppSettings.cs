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
    /// Automation only ever runs Normal or Moderate tier cleans (never Custom/Advanced/
    /// Experimental) — a deliberate safety choice: actions with real tradeoffs shouldn't
    /// fire unattended without the user present to notice if something feels off.
    /// </summary>
    public RamSavior.Core.Automation.AutomationTrigger Automation { get; set; } = new();

    /// <summary>Which tier automation uses when it fires. Restricted to Normal/Moderate at the UI level.</summary>
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
}
