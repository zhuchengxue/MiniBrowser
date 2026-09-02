using System.Windows;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private SettingsService? _settingsService;
    private AppSettings? _settings;
    private AdBlockService? _adBlockService;
    private string _cosmeticScript = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceService("MiniBrowser.SingleInstance");
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SignalPrimary();
            Shutdown();
            return;
        }

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
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _adBlockService = new AdBlockService(_settings.CustomBlockedHosts);
        _adBlockService.LoadEasyListLite(RuntimePaths.EasyListLitePath);
        _cosmeticScript = _adBlockService.CreateCosmeticScript(BrowserCompatibilityPolicy.AdBlockBypassHosts);
        if (_settings.Windows.Count == 0)
        {
            var profile = new WindowProfile
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
            };
            var tab = new TabProfile { Url = profile.Url };
            profile.Tabs.Add(tab);
            profile.ActiveTabId = tab.Id;
            _settings.Windows.Add(profile);
        }

        var window = new MainWindow(_settingsService, _settings, _settings.Windows[0], _adBlockService, _cosmeticScript, enableHotkey: true);
        _singleInstance.StartListening(() => Dispatcher.BeginInvoke(window.ShowFromExternalActivation));
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void SaveSettings()
    {
        if (_settings is not null)
        {
            _settingsService?.Save(_settings);
        }
    }
}
