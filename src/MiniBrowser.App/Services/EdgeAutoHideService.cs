using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MiniBrowser.App.Services;

public sealed class EdgeAutoHideService : IDisposable
{
    internal const double SnapMargin = 10d;
    internal const double VisibleStrip = 4d;

    private readonly Window _window;
    private readonly Func<bool> _isEnabled;
    private readonly Action? _hidden;
    private readonly Action? _revealed;
    private readonly DispatcherTimer _timer;
    private bool _isHidden;
    private bool _revealArmed;
    private NativeRect _restoreBounds;
    private EdgeSide _hiddenSide;
    private DateTime _ignoreHideUntilUtc;
    private DateTime _ignoreRevealUntilUtc;
    private EdgeSide _candidateEdge;
    private DateTime _candidateSinceUtc;

    public EdgeAutoHideService(Window window, Func<bool> isEnabled, Action? hidden = null, Action? revealed = null)
    {
        _window = window;
        _isEnabled = isEnabled;
        _hidden = hidden;
        _revealed = revealed;
        _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += Timer_Tick;
    }

    public bool IsHidden => _isHidden;

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Reveal()
    {
        if (!_isHidden)
        {
            return;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            MoveWindow(handle, _restoreBounds);
        }

        _isHidden = false;
        _hiddenSide = EdgeSide.None;
        _revealArmed = false;
        _candidateEdge = EdgeSide.None;
        _ignoreHideUntilUtc = DateTime.UtcNow.AddMilliseconds(700);
        _revealed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isEnabled() ||
            !_window.IsVisible ||
            _window.WindowState != WindowState.Normal ||
            IsAnyContextMenuOpen())
        {
            return;
        }

        if (_isHidden)
        {
            var now = DateTime.UtcNow;
            var cursorOnStrip = IsCursorOnVisibleStrip();
            if (!cursorOnStrip)
            {
                _revealArmed = true;
                return;
            }

            if (_revealArmed && now >= _ignoreRevealUntilUtc)
            {
                Reveal();
            }

            return;
        }

        if (_window.IsMouseOver)
        {
            return;
        }

        if (DateTime.UtcNow < _ignoreHideUntilUtc)
        {
            return;
        }

        var side = GetSnappedEdge();
        if (side == EdgeSide.None)
        {
            _candidateEdge = EdgeSide.None;
            return;
        }

        if (_candidateEdge != side)
        {
            _candidateEdge = side;
            _candidateSinceUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _candidateSinceUtc >= TimeSpan.FromMilliseconds(500))
        {
            _candidateEdge = EdgeSide.None;
            Hide(side);
        }
    }

    private bool IsCursorOnVisibleStrip()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero ||
            !GetWindowRect(handle, out var rect) ||
            !GetCursorPos(out var point))
        {
            return false;
        }

        return IsPointOnVisibleStrip(rect, point, _hiddenSide);
    }

    private bool IsAnyContextMenuOpen()
    {
        return _window.ContextMenu?.IsOpen == true;
    }

    private EdgeSide GetSnappedEdge()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var nativeRect))
        {
            return EdgeSide.None;
        }

        // Use the native rectangle for detection so DPI scaling and native moves
        // (including snap layouts) do not leave WPF coordinates stale.
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var work = screen.WorkingArea;

        if (Math.Abs(nativeRect.Left - work.Left) <= SnapMargin)
        {
            return EdgeSide.Left;
        }

        if (Math.Abs(nativeRect.Right - work.Right) <= SnapMargin)
        {
            return EdgeSide.Right;
        }

        if (Math.Abs(nativeRect.Top - work.Top) <= SnapMargin)
        {
            return EdgeSide.Top;
        }

        if (Math.Abs(nativeRect.Bottom - work.Bottom) <= SnapMargin)
        {
            return EdgeSide.Bottom;
        }

        return EdgeSide.None;
    }

    private void Hide(EdgeSide side)
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out _restoreBounds))
        {
            return;
        }

        _isHidden = true;
        _hiddenSide = side;
        _revealArmed = false;
        _candidateEdge = EdgeSide.None;
        _ignoreRevealUntilUtc = DateTime.UtcNow.AddMilliseconds(500);

        MoveWindow(handle, GetHiddenBounds(_restoreBounds, side));
        _hidden?.Invoke();
    }

    internal static NativeRect GetHiddenBounds(NativeRect restoreBounds, EdgeSide side)
    {
        var hiddenBounds = restoreBounds;
        switch (side)
        {
            case EdgeSide.Left:
                hiddenBounds.Offset((int)(-restoreBounds.Width + VisibleStrip), 0);
                break;
            case EdgeSide.Right:
                hiddenBounds.Offset((int)(restoreBounds.Width - VisibleStrip), 0);
                break;
            case EdgeSide.Top:
                hiddenBounds.Offset(0, (int)(-restoreBounds.Height + VisibleStrip));
                break;
            case EdgeSide.Bottom:
                hiddenBounds.Offset(0, (int)(restoreBounds.Height - VisibleStrip));
                break;
        }

        return hiddenBounds;
    }

    internal static bool IsPointOnVisibleStrip(NativeRect rect, NativePoint point, EdgeSide side)
    {
        const int padding = 3;
        return side switch
        {
            EdgeSide.Left => point.X >= rect.Right - VisibleStrip - padding &&
                             point.X <= rect.Right + padding &&
                             point.Y >= rect.Top - padding &&
                             point.Y <= rect.Bottom + padding,
            EdgeSide.Right => point.X >= rect.Left - padding &&
                              point.X <= rect.Left + VisibleStrip + padding &&
                              point.Y >= rect.Top - padding &&
                              point.Y <= rect.Bottom + padding,
            EdgeSide.Top => point.X >= rect.Left - padding &&
                            point.X <= rect.Right + padding &&
                            point.Y >= rect.Bottom - VisibleStrip - padding &&
                            point.Y <= rect.Bottom + padding,
            EdgeSide.Bottom => point.X >= rect.Left - padding &&
                               point.X <= rect.Right + padding &&
                               point.Y >= rect.Top - padding &&
                               point.Y <= rect.Top + VisibleStrip + padding,
            _ => false
        };
    }

    internal enum EdgeSide
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private static void MoveWindow(IntPtr handle, NativeRect bounds)
    {
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        SetWindowPos(
            handle,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            noZOrder | noActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public void Offset(int x, int y)
        {
            Left += x;
            Right += x;
            Top += y;
            Bottom += y;
        }
    }
}
