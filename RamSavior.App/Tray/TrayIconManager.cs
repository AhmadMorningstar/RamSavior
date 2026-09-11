using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RamSavior.App.Tray;

/// <summary>
/// Deliberately isolated to only System.Drawing / System.Windows.Forms usings — mixing
/// those with System.Windows (WPF) in the same file is exactly what caused the
/// ambiguous TextBlock error earlier in this project, so this file stays WPF-free and
/// callers interact with it through plain events/methods instead.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OpenRequested;
    public event Action? CleanNowRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open RAM Savior", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Clean Now", null, (_, _) => CleanNowRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "RAM Savior",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>Small amber circle drawn at runtime so we don't need to ship a separate .ico asset.</summary>
    private static Icon BuildIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(255, 0xFF, 0xB8, 0x00));
            g.FillEllipse(brush, 2, 2, 28, 28);
        }

        nint hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void UpdateTooltip(string text)
    {
        // NotifyIcon.Text has a 63-character limit.
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
