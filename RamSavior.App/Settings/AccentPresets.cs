namespace RamSavior.App.Settings;

public record AccentPreset(string Name, string Hex);

public static class AccentPresets
{
    /// <summary>The out-of-the-box accent (Amber). Also what "Reset" returns to.</summary>
    public const string DefaultHex = "#FFB800";

    // Ordered around the colour wheel (warm -> cool -> neutral) so the swatch grid reads
    // as a tidy gradient instead of a random scatter.
    public static readonly AccentPreset[] All =
    [
        new("Amber",    DefaultHex), // default
        new("Orange",   "#F97316"),
        new("Coral",    "#FF7F50"),
        new("Red",      "#EF4444"),
        new("Rose",     "#F43F5E"),
        new("Pink",     "#EC4899"),
        new("Fuchsia",  "#D946EF"),
        new("Violet",   "#8B5CF6"),
        new("Indigo",   "#6366F1"),
        new("Blue",     "#3B82F6"),
        new("Sky",      "#0EA5E9"),
        new("Cyan",     "#06B6D4"),
        new("Teal",     "#14B8A6"),
        new("Emerald",  "#10B981"),
        new("Green",    "#22C55E"),
        new("Lime",     "#84CC16"),
        new("Slate",    "#64748B"),
        new("Graphite", "#78716C"),
    ];

    /// <summary>Preset name for a hex value, or null when it's a custom colour.</summary>
    public static string? NameFor(string hex) =>
        All.FirstOrDefault(p => string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase))?.Name;
}
