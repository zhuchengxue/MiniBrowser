using System.Windows;
using System.Windows.Threading;

namespace MiniBrowser.App.Services;

public sealed class EdgeAutoHideService : IDisposable
{
    private const double SnapMargin = 10d;
    private const double VisibleStrip = 4d;

    private readonly Window _window;
    private readonly Func<bool> _isEnabled;
    private readonly Func<bool> _shouldSuppress;
    private readonly DispatcherTimer _timer;
    private bool _isHidden;
    private Rect _restoreBounds;

    public EdgeAutoHideService(Window window, Func<bool> isEnabled, Func<bool> shouldSuppress)
    {
        _window = window;
        _isEnabled = isEnabled;
        _shouldSuppress = shouldSuppress;
        _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += Timer_Tick;
        _window.MouseEnter += Window_MouseEnter;
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

        _window.Left = _restoreBounds.Left;
        _window.Top = _restoreBounds.Top;
        _window.Width = _restoreBounds.Width;
        _window.Height = _restoreBounds.Height;
        _isHidden = false;
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Timer_Tick;
        _window.MouseEnter -= Window_MouseEnter;
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Reveal();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isEnabled() ||
            !_window.IsVisible ||
            _window.WindowState != WindowState.Normal ||
            _isHidden ||
            _window.IsMouseOver ||
            _shouldSuppress())
        {
            return;
        }

        var side = GetSnappedEdge();
        if (side != EdgeSide.None)
        {
            Hide(side);
        }
    }

    private EdgeSide GetSnappedEdge()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_window.Left, (int)_window.Top));
        var work = screen.WorkingArea;
        var source = PresentationSource.FromVisual(_window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(work.Left, work.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(work.Right, work.Bottom));
        var right = _window.Left + WindowWidth();
        var bottom = _window.Top + WindowHeight();

        if (Math.Abs(_window.Left - topLeft.X) <= SnapMargin)
        {
            return EdgeSide.Left;
        }

        if (Math.Abs(right - bottomRight.X) <= SnapMargin)
        {
            return EdgeSide.Right;
        }

        if (Math.Abs(_window.Top - topLeft.Y) <= SnapMargin)
        {
            return EdgeSide.Top;
        }

        if (Math.Abs(bottom - bottomRight.Y) <= SnapMargin)
        {
            return EdgeSide.Bottom;
        }

        return EdgeSide.None;
    }

    private void Hide(EdgeSide side)
    {
        _restoreBounds = new Rect(_window.Left, _window.Top, WindowWidth(), WindowHeight());
        _isHidden = true;

        switch (side)
        {
            case EdgeSide.Left:
                _window.Left = _restoreBounds.Left - _restoreBounds.Width + VisibleStrip;
                break;
            case EdgeSide.Right:
                _window.Left = _restoreBounds.Right - VisibleStrip;
                break;
            case EdgeSide.Top:
                _window.Top = _restoreBounds.Top - _restoreBounds.Height + VisibleStrip;
                break;
            case EdgeSide.Bottom:
                _window.Top = _restoreBounds.Bottom - VisibleStrip;
                break;
        }
    }

    private double WindowWidth()
    {
        return _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
    }

    private double WindowHeight()
    {
        return _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
    }

    private enum EdgeSide
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }
}
