using System.Windows;
using System.IO;
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
            _showAboveTray(Forms.Cursor.Position);
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
