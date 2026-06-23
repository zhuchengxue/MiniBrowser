using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiniBrowser.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D42;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private const int WmHotkey = 0x0312;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;
    public bool IsRegistered => _registered;

    public HotkeyService(Window window)
    {
        _window = window;
    }

    public void Register()
    {
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(helper.Handle, HotkeyId, ModControl | ModShift, VkSpace);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var helper = new WindowInteropHelper(_window);
        if (_registered)
        {
            UnregisterHotKey(helper.Handle, HotkeyId);
        }

        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
