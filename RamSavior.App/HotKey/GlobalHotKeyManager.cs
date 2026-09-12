using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RamSavior.App.HotKey;

/// <summary>
/// Fixed to Ctrl+Alt+R for this release — a proper key-capture UI for letting the user
/// pick their own combo is real work (recording key-down events, validating conflicts)
/// that didn't fit this round; flagged as a good next step rather than half-built.
/// </summary>
public sealed class GlobalHotKeyManager : IDisposable
{
    private const int HOTKEY_ID = 0x4A51; // arbitrary app-unique id
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_R = 0x52;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotKeyPressed;

    public GlobalHotKeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("Window handle not available yet — construct after the window is shown.");
        _source.AddHook(WndProc);
    }

    public bool Register()
    {
        _registered = RegisterHotKey(_source.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_R);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotKeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HOTKEY_ID);
        _source.RemoveHook(WndProc);
    }
}
