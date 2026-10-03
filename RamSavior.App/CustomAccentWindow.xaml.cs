using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RamSavior.App.Settings;
using Wpf.Ui.Controls;

// UseWindowsForms + ImplicitUsings pull System.Drawing / System.Windows.Forms into every
// file, so these names are ambiguous with their WPF twins. Pin them to the WPF versions.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace RamSavior.App;

/// <summary>
/// Free-choice accent picker (Experimental mode only — Settings gates access). Nothing
/// touches the live app while picking: the caller reads <see cref="ChosenHex"/> after the
/// dialog closes, and null means the user cancelled.
/// </summary>
public partial class CustomAccentWindow : FluentWindow
{
    // Must match the fixed sizes declared in the XAML.
    private const double SvWidth = 270, SvHeight = 180, HueWidth = 270;

    private static readonly Regex HexPattern = new("^#?([0-9a-fA-F]{6})$", RegexOptions.Compiled);

    private double _hue;        // 0..360
    private double _sat = 1;    // 0..1
    private double _val = 1;    // 0..1
    private bool _updatingHex;

    /// <summary>The picked color as "#RRGGBB", or null if the dialog was cancelled.</summary>
    public string? ChosenHex { get; private set; }

    public CustomAccentWindow(string currentHex)
    {
        InitializeComponent();
        Icon = AppIcons.Window;

        Color start = TryParse(currentHex) ?? ParseOrDefault(AccentPresets.DefaultHex);
        SetFromColor(start);

        OldPreview.Background = new SolidColorBrush(start);
        OldPreviewText.Foreground = ReadableOn(start);

        Refresh(updateHexText: true);
    }

    // ----- Picking -----

    private void SvBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SvBox.CaptureMouse();
        PickSv(e.GetPosition(SvBox));
    }

    private void SvBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (SvBox.IsMouseCaptured) PickSv(e.GetPosition(SvBox));
    }

    private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        PickHue(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured) PickHue(e.GetPosition(HueBar));
    }

    private void Drag_MouseUp(object sender, MouseButtonEventArgs e) =>
        ((UIElement)sender).ReleaseMouseCapture();

    private void PickSv(Point p)
    {
        _sat = Math.Clamp(p.X / SvWidth, 0, 1);
        _val = 1 - Math.Clamp(p.Y / SvHeight, 0, 1);
        Refresh(updateHexText: true);
    }

    private void PickHue(Point p)
    {
        _hue = Math.Clamp(p.X / HueWidth, 0, 1) * 360;
        Refresh(updateHexText: true);
    }

    private void HexBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingHex) return; // our own write-back, not the user typing

        Color? parsed = TryParse(HexBox.Text);
        if (parsed is null) return; // half-typed — wait until it's a full valid color

        SetFromColor(parsed.Value);
        Refresh(updateHexText: false);
    }

    // ----- Buttons -----

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        SetFromColor(ParseOrDefault(AccentPresets.DefaultHex));
        Refresh(updateHexText: true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ChosenHex = ToHex(CurrentColor());
        Close();
    }

    // ----- Rendering -----

    private void Refresh(bool updateHexText)
    {
        Color color = CurrentColor();

        NewPreview.Background = new SolidColorBrush(color);
        NewPreviewText.Foreground = ReadableOn(color);

        SvBox.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));

        System.Windows.Controls.Canvas.SetLeft(SvThumb, _sat * SvWidth - SvThumb.Width / 2);
        System.Windows.Controls.Canvas.SetTop(SvThumb, (1 - _val) * SvHeight - SvThumb.Height / 2);
        System.Windows.Controls.Canvas.SetLeft(HueThumb, _hue / 360 * HueWidth - HueThumb.Width / 2);

        if (updateHexText)
        {
            _updatingHex = true;
            HexBox.Text = ToHex(color);
            _updatingHex = false;
        }
    }

    private void SetFromColor(Color c)
    {
        (double h, double s, double v) = ToHsv(c);
        // A grey/black/white has no meaningful hue — keep the old one so the hue thumb
        // doesn't snap back to red every time the user types a neutral color.
        if (s > 0 && v > 0) _hue = h;
        _sat = s;
        _val = v;
    }

    private Color CurrentColor() => FromHsv(_hue, _sat, _val);

    // ----- Color math -----

    private static Color? TryParse(string text)
    {
        Match m = HexPattern.Match(text.Trim());
        if (!m.Success) return null;
        string h = m.Groups[1].Value;
        return Color.FromRgb(
            Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16));
    }

    private static Color ParseOrDefault(string hex) => TryParse(hex) ?? Colors.Orange;

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Black or white text, whichever reads better on the given background.</summary>
    private static Brush ReadableOn(Color c)
    {
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return luminance > 0.6 ? Brushes.Black : Brushes.White;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;

        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x) // 5, and h == 360
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }

        return (h, max == 0 ? 0 : delta / max, max);
    }
}
