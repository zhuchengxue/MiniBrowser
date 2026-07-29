using System.Windows;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using MiniBrowser.App.Infrastructure;

namespace MiniBrowser.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly Action _exit;
    private readonly Action? _toggleFrame;
    private readonly Action? _showControls;
    private readonly Action<System.Drawing.Point>? _showAboveTray;
    private readonly Forms.NotifyIcon _notifyIcon;
    private System.Drawing.Point? _lastAnchorPoint;

    public TrayService(
        Window window,
        Action exit,
        Action? toggleFrame = null,
        Action? showControls = null,
        Action<System.Drawing.Point>? showAboveTray = null)
    {
        _window = window;
        _exit = exit;
        _toggleFrame = toggleFrame;
        _showControls = showControls;
        _showAboveTray = showAboveTray;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "MiniBrowser",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.DoubleClick += (_, _) => ShowAboveTrayOrWindow();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = RuntimePaths.AppIconPath;
        return File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowWindow());
        if (_showAboveTray is not null)
        {
            menu.Items.Add("Show above tray", null, (_, _) => ShowAboveTrayOrWindow());
        }

        menu.Items.Add("Hide", null, (_, _) => _window.Hide());
        if (_toggleFrame is not null)
        {
            menu.Items.Add("Toggle frame", null, (_, _) => _toggleFrame());
        }

        if (_showControls is not null)
        {
            menu.Items.Add("Show controls", null, (_, _) =>
            {
                ShowWindow();
                _showControls();
            });
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _notifyIcon.Visible = false;
            _exit();
        });
        return menu;
    }

    private void NotifyIcon_MouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowAboveTrayOrWindow();
        }
    }

    private void ShowAboveTrayOrWindow()
    {
        if (_showAboveTray is not null)
        {
            var anchor = GetTrayAnchorPoint();
            _lastAnchorPoint = anchor;
            _showAboveTray(anchor);
            return;
        }

        ShowWindow();
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public System.Drawing.Point GetTrayAnchorPoint()
    {
        if (TryGetNotifyIconCenter(out var center))
        {
            _lastAnchorPoint = center;
            return center;
        }

        if (_lastAnchorPoint is { } last)
        {
            return last;
        }

        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.FromPoint(Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        return new System.Drawing.Point(work.Right - 24, bounds.Bottom - Math.Max(12, (bounds.Bottom - work.Bottom) / 2));
    }

    private bool TryGetNotifyIconCenter(out System.Drawing.Point center)
    {
        center = default;
        try
        {
            var windowField = typeof(Forms.NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic) ??
                              typeof(Forms.NotifyIcon).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
            var idField = typeof(Forms.NotifyIcon).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic) ??
                          typeof(Forms.NotifyIcon).GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
            var window = windowField?.GetValue(_notifyIcon);
            var handleProperty = window?.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handleProperty?.GetValue(window) is not IntPtr handle || idField?.GetValue(_notifyIcon) is not int id)
            {
                return false;
            }

            var identifier = new NotifyIconIdentifier
            {
                cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
                hWnd = handle,
                uID = (uint)id
            };

            if (Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
            {
                return false;
            }

            center = new System.Drawing.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
            return center.X != 0 || center.Y != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);
}
