namespace RamSavior.App.Settings;

public record AccentPreset(string Name, string Hex);

public static class AccentPresets
{
    public static readonly AccentPreset[] All =
    [
        new("Amber",   "#FFB800"), // current default
        new("Blue",    "#3B82F6"),
        new("Emerald", "#10B981"),
        new("Violet",  "#8B5CF6"),
        new("Rose",    "#F43F5E"),
        new("Slate",   "#64748B"),
    ];
}
