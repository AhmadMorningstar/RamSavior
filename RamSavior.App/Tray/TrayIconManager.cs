using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace RamSavior.App.Tray;

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
            Icon = LoadAppIcon(),
            Text = "RAM Savior",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            // Pack URI pointing to the embedded WPF resource inside the assembly
            var uri = new Uri("pack://application:,,,/Assets/Icons/app.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);

            if (streamInfo?.Stream != null)
            {
                return new Icon(streamInfo.Stream);
            }
        }
        catch
        {
            // Fall back to system icon if resource resolution fails
        }

        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void UpdateTooltip(string text)
    {
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}