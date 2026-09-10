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
}
