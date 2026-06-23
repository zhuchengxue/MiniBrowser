using System.Windows;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class App : System.Windows.Application
{
    private readonly List<MainWindow> _windows = [];
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogger.Error(ex, "Unhandled domain exception.");
            }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "Unhandled UI exception.");
            args.Handled = true;
            System.Windows.MessageBox.Show(args.Exception.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppLogger.Info($"{AppInfo.ProductName} {AppInfo.Version} starting.");
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        if (_settings.Windows.Count == 0)
        {
            _settings.Windows.Add(new WindowProfile
            {
                Url = string.IsNullOrWhiteSpace(_settings.LastUrl) ? _settings.HomeUrl : _settings.LastUrl,
                Width = _settings.WindowWidth,
                Height = _settings.WindowHeight,
                Left = _settings.WindowLeft,
                Top = _settings.WindowTop,
                Opacity = _settings.WindowOpacity,
                Topmost = _settings.Topmost,
                MobileMode = _settings.MobileMode,
                ChromeVisible = _settings.ChromeVisible,
                SizePresetIndex = _settings.SizePresetIndex
            });
        }

        foreach (var profile in _settings.Windows.ToList())
        {
            OpenWindow(profile);
        }
    }

    public void OpenWindow(WindowProfile? profile = null)
    {
        if (_settings is null || _settingsService is null)
        {
            return;
        }

        profile ??= new WindowProfile { Url = _settings.HomeUrl };
        if (!_settings.Windows.Any(window => window.Id == profile.Id))
        {
            _settings.Windows.Add(profile);
            _settingsService.Save(_settings);
        }

        var window = new MainWindow(_settingsService, _settings, profile, _windows.Count == 0);
        window.Closed += (_, _) => _windows.Remove(window);
        _windows.Add(window);
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void RemoveProfile(WindowProfile profile)
    {
        _settings?.Windows.RemoveAll(window => window.Id == profile.Id);
        if (_settings is not null)
        {
            if (_settings.Windows.Count == 0)
            {
                _settings.Windows.Add(new WindowProfile { Url = _settings.HomeUrl });
            }

            _settingsService?.Save(_settings);
        }
    }

    public void SaveSettings()
    {
        if (_settings is not null)
        {
            _settingsService?.Save(_settings);
        }
    }
}
