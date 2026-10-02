using System.Reflection;

namespace RamSavior.Core;

/// <summary>
/// Single access point for every asset shipped inside RamSavior.Core (everything under
/// the Assets folder is an embedded resource). The GUI, tray and any future front end
/// load assets through here instead of keeping their own copies.
/// </summary>
public static class AppAssets
{
    private static readonly Assembly CoreAssembly = typeof(AppAssets).Assembly;

    /// <summary>
    /// Opens an asset by its path relative to the Assets folder, e.g. "Icons/app.ico".
    /// The caller owns (and must dispose) the returned stream.
    /// </summary>
    public static Stream OpenAsset(string relativePath)
    {
        string resourceName = $"{CoreAssembly.GetName().Name}.Assets.{relativePath.Replace('/', '.').Replace('\\', '.')}";

        Stream? stream = CoreAssembly.GetManifestResourceStream(resourceName);
        if (stream is not null) return stream;

        // Fallback: tolerate namespace/RootNamespace differences by matching the tail.
        string tail = $"Assets.{relativePath.Replace('/', '.').Replace('\\', '.')}";
        string? match = CoreAssembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(tail, StringComparison.OrdinalIgnoreCase));

        return match is not null
            ? CoreAssembly.GetManifestResourceStream(match)!
            : throw new FileNotFoundException($"Asset '{relativePath}' not found in RamSavior.Core.");
    }
}
