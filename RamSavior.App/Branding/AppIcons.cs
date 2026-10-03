using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RamSavior.Core;

namespace RamSavior.App;

/// <summary>
/// WPF-side view of the icon owned by RamSavior.Core. Decoded once, frozen (so it can be
/// shared by every window and across threads) and cached.
/// </summary>
internal static class AppIcons
{
    private static readonly Lazy<ImageSource?> WindowIconLazy = new(() =>
    {
        try
        {
            using Stream stream = AppAssets.OpenAsset("Icons/app.ico");
            // OnLoad copies the bits so the stream can be disposed right away.
            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null; // a missing icon must never stop a window from opening
        }
    });

    public static ImageSource? Window => WindowIconLazy.Value;
}
