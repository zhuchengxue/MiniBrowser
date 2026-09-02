using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiniBrowser.App.Services;

public sealed class EdgeAutoHideService : IDisposable
{
    internal const double SnapMargin = 32d;
    internal const double VisibleStrip = 4d;

    private readonly Window _window;
    private readonly Func<bool> _isEnabled;
    private readonly Action? _hidden;
    private readonly Action? _revealed;
    private readonly object _sync = new();
    private readonly System.Windows.Forms.Timer _timer;
    private System.Threading.Timer? _revealTimer;
    private IntPtr _handle;
    private bool _isHidden;
    private bool _transitioning;
    private bool _autoHideArmed;
    private NativeRect _restoreBounds;
    private System.Drawing.Rectangle _hiddenWorkArea;
    private EdgeSide _hiddenSide;
    private DateTime _lastAutoActionUtc = DateTime.MinValue;

    public EdgeAutoHideService(Window window, Func<bool> isEnabled, Action? hidden = null, Action? revealed = null)
    {
        _window = window;
        _isEnabled = isEnabled;
        _hidden = hidden;
        _revealed = revealed;
        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += Timer_Tick;
    }

    public bool IsHidden => _isHidden;

    public void Start()
    {
        if (_timer.Enabled)
        {
            return;
        }

        _handle = new WindowInteropHelper(_window).Handle;
        _lastAutoActionUtc = DateTime.UtcNow;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        StopRevealTimer();
    }

    public void Reveal()
    {
        lock (_sync)
        {
            if (!_isHidden)
            {
                return;
            }

            var handle = CurrentHandle();
            if (handle == IntPtr.Zero || !MoveWindow(handle, _restoreBounds))
            {
                return;
            }

            _isHidden = false;
            _hiddenSide = EdgeSide.None;
            _hiddenWorkArea = System.Drawing.Rectangle.Empty;
            _autoHideArmed = IsCursorOverBounds(_restoreBounds);
            _lastAutoActionUtc = DateTime.UtcNow;
            StopRevealTimer();
        }

        NotifyOnWindowThread(_revealed);
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!Monitor.TryEnter(_sync))
        {
            return;
        }

        try
        {
            var handle = CurrentHandle();
            if (!_isEnabled() ||
                handle == IntPtr.Zero ||
                !IsWindowVisible(handle) ||
                IsIconic(handle) ||
                IsAnyContextMenuOpen())
            {
                return;
            }

            if (_isHidden)
            {
                if (PointerInCollapsedTrigger())
                {
                    RevealFromTimer();
                }

                return;
            }

            if (!_autoHideArmed)
            {
                // A reveal can leave the pointer on the 4px trigger outside the
                // restored window. Re-arm only after the user actually enters
                // the window, otherwise it hides again after about one second.
                _autoHideArmed = ShouldArmAutoHide(_autoHideArmed, IsCursorOverWindow());

                return;
            }

            var side = GetSnappedEdge();
            if (side == EdgeSide.Top && PointerNearTopTrigger())
            {
                return;
            }

            if (!IsCursorOverWindow() && side != EdgeSide.None)
            {
                Hide(side);
            }
        }
        catch (Exception ex)
        {
            MiniBrowser.App.Infrastructure.AppLogger.Error(ex, "Edge auto-hide timer failed.");
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private bool PointerNearTopTrigger()
    {
        var handle = CurrentHandle();
        if (handle == IntPtr.Zero ||
            !GetWindowRect(handle, out var rect) ||
            !GetCursorPos(out var point))
        {
            return false;
        }

        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        return point.Y >= work.Top &&
               point.Y <= work.Top + 18 &&
               point.X >= rect.Left &&
               point.X <= rect.Right;
    }

    private bool PointerInCollapsedTrigger()
    {
        if (_hiddenWorkArea.IsEmpty || !GetCursorPos(out var point))
        {
            return false;
        }

        const int trigger = 12;
        return _hiddenSide switch
        {
            EdgeSide.Right => point.X >= _hiddenWorkArea.Right - trigger &&
                              point.X < _hiddenWorkArea.Right &&
                              point.Y >= _restoreBounds.Top &&
                              point.Y <= _restoreBounds.Bottom,
            EdgeSide.Left => point.X >= _hiddenWorkArea.Left &&
                             point.X <= _hiddenWorkArea.Left + trigger &&
                             point.Y >= _restoreBounds.Top &&
                             point.Y <= _restoreBounds.Bottom,
            EdgeSide.Top => point.Y >= _hiddenWorkArea.Top &&
                            point.Y <= _hiddenWorkArea.Top + trigger &&
                            point.X >= _restoreBounds.Left &&
                            point.X <= _restoreBounds.Right,
            EdgeSide.Bottom => point.Y >= _hiddenWorkArea.Bottom - trigger &&
                               point.Y < _hiddenWorkArea.Bottom &&
                               point.X >= _restoreBounds.Left &&
                               point.X <= _restoreBounds.Right,
            _ => false
        };
    }

    private bool IsCursorOverWindow()
    {
        var handle = CurrentHandle();
        if (handle == IntPtr.Zero ||
            !GetWindowRect(handle, out var rect))
        {
            return false;
        }

        return IsCursorOverBounds(rect);
    }

    private bool IsCursorOverBounds(NativeRect rect)
    {
        return GetCursorPos(out var point) &&
               point.X >= rect.Left &&
               point.X <= rect.Right &&
               point.Y >= rect.Top &&
               point.Y <= rect.Bottom;
    }

    private bool IsAnyContextMenuOpen()
    {
        return _window.Dispatcher.CheckAccess() && _window.ContextMenu?.IsOpen == true;
    }

    private EdgeSide GetSnappedEdge()
    {
        var handle = CurrentHandle();
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
        if (_isHidden || _transitioning ||
            DateTime.UtcNow - _lastAutoActionUtc < TimeSpan.FromMilliseconds(900))
        {
            return;
        }

        var handle = CurrentHandle();
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out _restoreBounds))
        {
            return;
        }

        _hiddenWorkArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        _restoreBounds = AlignRestoreBoundsToEdge(_restoreBounds, side, _hiddenWorkArea);
        if (!MoveWindow(handle, GetHiddenBoundsForWorkArea(_restoreBounds, side, _hiddenWorkArea)))
        {
            return;
        }

        _transitioning = true;
        _isHidden = true;
        _hiddenSide = side;
        _lastAutoActionUtc = DateTime.UtcNow;
        _transitioning = false;
        StartRevealTimer();
        NotifyOnWindowThread(_hidden);
    }

    private void RevealFromTimer()
    {
        if (!_isHidden || _transitioning ||
            DateTime.UtcNow - _lastAutoActionUtc < TimeSpan.FromMilliseconds(180))
        {
            return;
        }

        var handle = CurrentHandle();
        _transitioning = true;
        if (handle == IntPtr.Zero || !MoveWindow(handle, _restoreBounds))
        {
            _transitioning = false;
            return;
        }

        _isHidden = false;
        _hiddenSide = EdgeSide.None;
        _hiddenWorkArea = System.Drawing.Rectangle.Empty;
        _autoHideArmed = IsCursorOverBounds(_restoreBounds);
        _lastAutoActionUtc = DateTime.UtcNow;
        _transitioning = false;
        StopRevealTimer();
        NotifyOnWindowThread(_revealed);
    }

    private void StartRevealTimer()
    {
        if (_revealTimer is not null)
        {
            return;
        }

        _revealTimer = new System.Threading.Timer(RevealTimer_Tick, null, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80));
    }

    private void StopRevealTimer()
    {
        var timer = Interlocked.Exchange(ref _revealTimer, null);
        timer?.Dispose();
    }

    private void RevealTimer_Tick(object? state)
    {
        if (!Monitor.TryEnter(_sync))
        {
            return;
        }

        try
        {
            if (!_isHidden || _transitioning || !PointerInCollapsedTrigger())
            {
                return;
            }

            RevealFromTimer();
        }
        catch (Exception ex)
        {
            MiniBrowser.App.Infrastructure.AppLogger.Error(ex, "Edge auto-hide reveal timer failed.");
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private void NotifyOnWindowThread(Action? callback)
    {
        if (callback is null)
        {
            return;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            callback();
        }
        else
        {
            _window.Dispatcher.BeginInvoke(callback);
        }
    }

    private IntPtr CurrentHandle()
    {
        if (_handle != IntPtr.Zero)
        {
            return _handle;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            _handle = new WindowInteropHelper(_window).Handle;
        }

        return _handle;
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

    private static NativeRect AlignRestoreBoundsToEdge(
        NativeRect restoreBounds,
        EdgeSide side,
        System.Drawing.Rectangle work)
    {
        var alignedBounds = restoreBounds;

        switch (side)
        {
            case EdgeSide.Left:
                alignedBounds.Offset(work.Left - restoreBounds.Left, 0);
                break;
            case EdgeSide.Right:
                alignedBounds.Offset(work.Right - restoreBounds.Right, 0);
                break;
            case EdgeSide.Top:
                alignedBounds.Offset(0, work.Top - restoreBounds.Top);
                break;
            case EdgeSide.Bottom:
                alignedBounds.Offset(0, work.Bottom - restoreBounds.Bottom);
                break;
        }

        return alignedBounds;
    }

    private static NativeRect GetHiddenBoundsForWorkArea(
        NativeRect restoreBounds,
        EdgeSide side,
        System.Drawing.Rectangle work)
    {
        var hiddenBounds = restoreBounds;
        var strip = (int)VisibleStrip;

        switch (side)
        {
            case EdgeSide.Left:
                hiddenBounds.Left = work.Left + strip - restoreBounds.Width;
                hiddenBounds.Right = work.Left + strip;
                break;
            case EdgeSide.Right:
                hiddenBounds.Left = work.Right - strip;
                hiddenBounds.Right = work.Right - strip + restoreBounds.Width;
                break;
            case EdgeSide.Top:
                hiddenBounds.Top = work.Top + strip - restoreBounds.Height;
                hiddenBounds.Bottom = work.Top + strip;
                break;
            case EdgeSide.Bottom:
                hiddenBounds.Top = work.Bottom - strip;
                hiddenBounds.Bottom = work.Bottom - strip + restoreBounds.Height;
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

    internal static bool ShouldArmAutoHide(bool isArmed, bool cursorOverWindow)
    {
        return isArmed || cursorOverWindow;
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
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

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

    private static bool MoveWindow(IntPtr handle, NativeRect bounds)
    {
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        return SetWindowPos(
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
