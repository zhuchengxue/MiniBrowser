using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class MainWindow : Window
{
    private const string GoogleSearchUrl = "https://www.google.com/search?q={query}";
    private const string MobileUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    private static readonly WindowPreset[] SizePresets =
    [
        new("390x844", 390, 844),
        new("430x932", 430, 932),
        new("360x780", 360, 780),
        new("768x920", 768, 920)
    ];

    private static readonly double[] OpacityPresets = [1.0, 0.92, 0.84, 0.76];
    private const string LowMemoryBrowserArguments =
        "--disable-background-networking --disable-sync --disable-component-update " +
        "--disable-domain-reliability --metrics-recording-only " +
        "--disable-features=Translate,MediaRouter,OptimizationHints,AutofillServerCommunication";

    private readonly SettingsService _settingsService;
    private readonly AdBlockService _adBlockService;
    private readonly EdgeAutoHideService _edgeAutoHideService;
    private readonly string _cosmeticScript;
    private readonly AppSettings _settings;
    private readonly WindowProfile _profile;
    private readonly TrayService _trayService;
    private readonly HotkeyService? _hotkeyService;
    private readonly bool _isPrimaryWindow;
    private bool _hotkeyWarningShown;
    private bool _isReallyClosing;
    private bool _isEditingAddress;
    private bool _removeProfileOnClose;
    private bool _applyingSiteProfile;
    private bool _usesPopupStartupPosition;
    private int _blockedRequestCount;
    private System.Windows.Interop.HwndSource? _keyboardSource;

    public MainWindow(
        SettingsService settingsService,
        AppSettings settings,
        WindowProfile profile,
        AdBlockService adBlockService,
        string cosmeticScript,
        bool enableHotkey)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = settings;
        _profile = profile;
        _isPrimaryWindow = enableHotkey;
        _adBlockService = adBlockService;
        _cosmeticScript = cosmeticScript;
        _trayService = new TrayService(this, ExitApplication, ToggleBorderMode, ShowChrome, ShowAboveTray);
        _edgeAutoHideService = new EdgeAutoHideService(
            this,
            () => _settings.EdgeAutoHideEnabled);

        Width = _profile.Width;
        Height = _profile.Height;
        if (_isPrimaryWindow || _profile.Left < 0 || _profile.Top < 0)
        {
            _usesPopupStartupPosition = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            PositionPopupInWorkArea(SystemParameters.WorkArea);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _profile.Left;
            Top = _profile.Top;
        }

        Opacity = ClampOpacity(_profile.Opacity);
        Topmost = _profile.Topmost;
        if (!_profile.Borderless)
        {
            _profile.ChromeVisible = true;
        }

        QuickSites.ItemsSource = _settings.QuickSites;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Browser.PreviewKeyDown += Browser_KeyDown;
        Browser.KeyDown += Browser_KeyDown;
        SourceInitialized += MainWindow_SourceInitialized;
        if (enableHotkey && _settings.GlobalHotkeyEnabled)
        {
            SourceInitialized += (_, _) =>
            {
                _hotkeyService?.Register();
                if (_hotkeyService?.IsRegistered == false && !_hotkeyWarningShown)
                {
                    _hotkeyWarningShown = true;
                    StatusText.Text = "Global hotkey unavailable";
                }
            };
            _hotkeyService = new HotkeyService(this);
            _hotkeyService.Pressed += (_, _) => Dispatcher.BeginInvoke(ShowWindowAndFocusAddress, DispatcherPriority.Send);
        }

        UpdateToggleLabels();
        ApplyChromeVisibility();
        ApplyBorderMode();

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _keyboardSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
        _keyboardSource?.AddHook(WindowKeyboardHook);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Show();
            WindowState = WindowState.Normal;
            if (_usesPopupStartupPosition)
            {
                PositionPopup(_trayService.GetTrayAnchorPoint());
            }

            Activate();

            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = _settings.LowMemoryMode ? LowMemoryBrowserArguments : string.Empty
            };
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: RuntimePaths.WebView2DataDirectory,
                options);
            await Browser.EnsureCoreWebView2Async(environment);
            await ConfigureBrowserAsync();
            Navigate(string.IsNullOrWhiteSpace(_profile.Url) ? _settings.HomeUrl : _profile.Url);
            _edgeAutoHideService.Start();
            ScheduleStartupUpdateCheck();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "WebView2 startup failed.");
            StatusText.Text = "WebView2 startup failed";
            System.Windows.MessageBox.Show(
                "MiniBrowser could not start WebView2.\n\n" + ex.Message,
                "MiniBrowser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConfigureBrowserAsync()
    {
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        ApplyUserAgent();
        if (!string.IsNullOrWhiteSpace(_cosmeticScript))
        {
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_cosmeticScript);
        }

        AddAdBlockRequestFilters();
        Browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
        Browser.CoreWebView2.WebResourceRequested += Browser_WebResourceRequested;
        Browser.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!_isEditingAddress)
            {
                AddressBox.Text = args.Uri;
            }

            StatusText.Text = "Loading...";
            UpdateNavigationButtons();
        };
        Browser.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            StatusText.Text = args.IsSuccess ? "Ready" : $"Load failed: {args.WebErrorStatus}";
            if (!_isEditingAddress)
            {
                AddressBox.Text = Browser.Source?.ToString() ?? AddressBox.Text;
            }

            if (args.IsSuccess)
            {
                _profile.Url = Browser.Source?.ToString() ?? AddressBox.Text;
                _settings.LastUrl = _profile.Url;
                ApplySiteProfileForUrl(_profile.Url, saveWindowState: false);
                SaveSettings();
            }

            UpdateNavigationButtons();
        };
        Browser.CoreWebView2.SourceChanged += (_, _) =>
        {
            if (!_isEditingAddress)
            {
                AddressBox.Text = Browser.Source?.ToString() ?? AddressBox.Text;
            }

            UpdateNavigationButtons();
        };
    }

    private void AddAdBlockRequestFilters()
    {
        var contexts = new[]
        {
            CoreWebView2WebResourceContext.Document,
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceContext.Media,
            CoreWebView2WebResourceContext.Script,
            CoreWebView2WebResourceContext.XmlHttpRequest,
            CoreWebView2WebResourceContext.Fetch,
            CoreWebView2WebResourceContext.Ping,
            CoreWebView2WebResourceContext.Other
        };

        foreach (var context in contexts)
        {
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", context);
        }
    }

    private void Browser_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var enabled = IsAdBlockEnabledForUrl(e.Request.Uri);
        if (!_adBlockService.ShouldBlock(e.Request.Uri, enabled, _settings.AdBlockWhitelist))
        {
            return;
        }

        _blockedRequestCount++;
        e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream([]),
            204,
            "Blocked",
            "Content-Type: text/plain");
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            Navigate(e.Uri);
        }
    }

    private void Navigate(string raw)
    {
        var url = NormalizeUrl(raw);
        AddressBox.Text = url;
        ApplySiteProfileForUrl(url, saveWindowState: false);
        Browser.Source = new Uri(url);
    }

    private string NormalizeUrl(string raw)
    {
        var value = raw.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return _settings.HomeUrl;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (value.Contains('.') && !value.Contains(' '))
        {
            return "https://" + value;
        }

        return BuildSearchUrl(value);
    }

    private string BuildSearchUrl(string query)
    {
        var template = string.IsNullOrWhiteSpace(_settings.SearchEngineUrl)
            ? GoogleSearchUrl
            : _settings.SearchEngineUrl;
        if (!template.Contains("{query}", StringComparison.OrdinalIgnoreCase))
        {
            template = GoogleSearchUrl;
        }

        return template.Replace("{query}", Uri.EscapeDataString(query), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyUserAgent()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.Settings.UserAgent = _profile.MobileMode ? MobileUserAgent : string.Empty;
    }

    private void UpdateToggleLabels()
    {
        TopmostButton.Content = Topmost ? "\uE840" : "\uE718";
        MobileButton.Content = _profile.MobileMode ? "Phone" : "Desk";
        MenuButton.Content = "\uE700";
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        if (!IsLoaded)
        {
            return;
        }

        BackButton.IsEnabled = Browser.CanGoBack;
        ForwardButton.IsEnabled = Browser.CanGoForward;
        RefreshButton.IsEnabled = Browser.CoreWebView2 is not null;
    }

    private void ApplyChromeVisibility()
    {
        NavRow.Height = _profile.ChromeVisible ? new GridLength(58) : new GridLength(0);
        ToolsRow.Height = new GridLength(0);
        StatusText.Text = _profile.ChromeVisible ? StatusText.Text : "Clean mode";
    }

    private void ApplyBorderMode()
    {
        WindowStyle = _profile.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoForward)
        {
            Browser.GoForward();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Browser.Reload();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        Navigate(_settings.HomeUrl);
    }

    private void TopmostButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _profile.Topmost = Topmost;
        SaveSettings();
        UpdateToggleLabels();
    }

    private void MobileButton_Click(object sender, RoutedEventArgs e)
    {
        _profile.MobileMode = !_profile.MobileMode;
        ApplyUserAgent();
        SaveSettings();
        UpdateToggleLabels();
        Browser.Reload();
    }

    private void SizeButton_Click(object sender, RoutedEventArgs e)
    {
        _profile.SizePresetIndex = (_profile.SizePresetIndex + 1) % SizePresets.Length;
        ApplyWindowPreset(CurrentPreset());
        SaveSettings();
        UpdateToggleLabels();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (_hotkeyService is not null && !_hotkeyService.IsRegistered)
        {
            var hotkeyStatus = new MenuItem { Header = "Global hotkey unavailable", IsEnabled = false };
            menu.Items.Add(hotkeyStatus);
            menu.Items.Add(new Separator());
        }

        var home = new MenuItem { Header = "Home    Ctrl+H" };
        home.Click += (_, _) => Navigate(_settings.HomeUrl);
        menu.Items.Add(home);

        var newWindow = new MenuItem { Header = "New window    Ctrl+T" };
        newWindow.Click += (_, _) => OpenNewWindowFromCurrentPage();
        menu.Items.Add(newWindow);

        menu.Items.Add(new Separator());
        var phone = new MenuItem { Header = _profile.MobileMode ? "Desktop mode" : "Phone mode" };
        phone.Click += MobileButton_Click;
        menu.Items.Add(phone);

        var shield = new MenuItem { Header = IsCurrentSiteAdBlockEnabled() ? "Ad block: ON for this site" : "Ad block: OFF for this site" };
        shield.Click += ShieldButton_Click;
        menu.Items.Add(shield);

        var clean = new MenuItem { Header = _profile.ChromeVisible ? "Hide controls    F8" : "Show controls    F8" };
        clean.Click += ChromeButton_Click;
        menu.Items.Add(clean);

        menu.Items.Add(new Separator());
        var preferences = new MenuItem { Header = "Preferences" };
        preferences.Click += (_, _) =>
        {
            var settingsWindow = new SettingsWindow(_settingsService, _settings) { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                _adBlockService.ReplaceCustomBlockedHosts(_settings.CustomBlockedHosts);
                QuickSites.ItemsSource = _settings.QuickSites;
            }
        };
        menu.Items.Add(preferences);

        menu.Items.Add(new Separator());
        var hideWindow = new MenuItem { Header = "Hide    Ctrl+Shift+Space" };
        hideWindow.Click += (_, _) => Hide();
        menu.Items.Add(hideWindow);

        var closeWindow = new MenuItem { Header = "Close this window    Ctrl+W" };
        closeWindow.Click += (_, _) => CloseThisWindow();
        menu.Items.Add(closeWindow);

        menu.PlacementTarget = MenuButton;
        menu.IsOpen = true;
    }

    private void BorderButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleBorderMode();
    }

    private void ShieldButton_Click(object sender, RoutedEventArgs e)
    {
        var host = CurrentHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            _profile.AdBlockEnabled = !_profile.AdBlockEnabled;
        }
        else if (_settings.AdBlockWhitelist.Any(item => HostMatches(host, item)))
        {
            _settings.AdBlockWhitelist.RemoveAll(item => HostMatches(host, item));
            GetOrCreateSiteProfile(host).AdBlockEnabled = true;
        }
        else
        {
            GetOrCreateSiteProfile(host).AdBlockEnabled = !IsCurrentSiteAdBlockEnabled();
        }

        SaveSettings();
        UpdateToggleLabels();
        Browser.Reload();
    }

    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        var current = Array.FindIndex(OpacityPresets, value => Math.Abs(value - Opacity) < 0.01);
        var next = current < 0 ? 0 : (current + 1) % OpacityPresets.Length;
        Opacity = OpacityPresets[next];
        _profile.Opacity = Opacity;
        SaveSettings();
        UpdateToggleLabels();
    }

    private void ToggleLowMemoryMode()
    {
        _settings.LowMemoryMode = !_settings.LowMemoryMode;
        SaveSettings();
        StatusText.Text = _settings.LowMemoryMode ? "Low memory mode enabled" : "Low memory mode disabled";
        System.Windows.MessageBox.Show(
            "Low memory mode will take effect after restarting MiniBrowser.",
            "MiniBrowser",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task ClearRuntimeCacheAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Clearing cache...";
            await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache |
                CoreWebView2BrowsingDataKinds.CacheStorage |
                CoreWebView2BrowsingDataKinds.ServiceWorkers);
            StatusText.Text = "Cache cleared";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to clear WebView2 cache.");
            StatusText.Text = "Clear cache failed";
            System.Windows.MessageBox.Show(
                "MiniBrowser could not clear the runtime cache.\n\n" + ex.Message,
                "MiniBrowser",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_isPrimaryWindow || !_settings.AutoCheckUpdates)
        {
            return;
        }

        if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < TimeSpan.FromHours(24))
        {
            return;
        }

        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        SaveSettings();
        try
        {
            var result = await new UpdateService().CheckAsync();
            if (result.IsAvailable)
            {
                Dispatcher.Invoke(() => PromptForUpdate(result));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Startup update check failed.");
        }
    }

    private void ScheduleStartupUpdateCheck()
    {
        if (!_isPrimaryWindow || !_settings.AutoCheckUpdates)
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            await CheckForUpdatesOnStartupAsync();
        }, DispatcherPriority.ApplicationIdle);
    }

    private async Task CheckForUpdatesInteractiveAsync()
    {
        try
        {
            StatusText.Text = "Checking updates...";
            var result = await new UpdateService().CheckAsync();
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();

            if (result.IsUnavailable)
            {
                StatusText.Text = "Update check failed";
                System.Windows.MessageBox.Show(
                    "MiniBrowser could not check for updates.\n\n" + result.Error,
                    "MiniBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!result.IsAvailable)
            {
                StatusText.Text = "MiniBrowser is up to date";
                System.Windows.MessageBox.Show(
                    $"MiniBrowser {AppInfo.Version} is up to date.",
                    "MiniBrowser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            PromptForUpdate(result);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Interactive update check failed.");
            StatusText.Text = "Update check failed";
            System.Windows.MessageBox.Show(
                "MiniBrowser could not check for updates.\n\n" + ex.Message,
                "MiniBrowser",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void PromptForUpdate(UpdateCheckResult result)
    {
        var message = $"MiniBrowser {result.VersionTag} is available.\n\nCurrent version: {AppInfo.Version}";
        if (result.Asset is null)
        {
            var openRelease = System.Windows.MessageBox.Show(
                message + "\n\nNo portable update package was found in the release. Open the release page?",
                "MiniBrowser Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (openRelease == MessageBoxResult.Yes)
            {
                OpenExternalUrl(result.ReleaseUrl);
            }

            return;
        }

        var answer = System.Windows.MessageBox.Show(
            message + "\n\nDownload and install this update now? MiniBrowser will restart.",
            "MiniBrowser Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await DownloadAndApplyUpdateAsync(result.Asset);
    }

    private async Task DownloadAndApplyUpdateAsync(UpdateAsset asset)
    {
        try
        {
            var progress = new Progress<double>(value => StatusText.Text = $"Downloading update {Math.Round(value * 100)}%...");
            var updateService = new UpdateService();
            var zipPath = await updateService.DownloadAsync(asset, progress);
            var scriptPath = updateService.PrepareUpdaterScript(zipPath);
            SaveSettings();
            updateService.LaunchUpdater(scriptPath);
            _isReallyClosing = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Update download/apply failed.");
            StatusText.Text = "Update failed";
            System.Windows.MessageBox.Show(
                "MiniBrowser could not install the update.\n\n" + ex.Message,
                "MiniBrowser",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void ChromeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleChrome();
    }

    private void AddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _isEditingAddress = false;
            Navigate(AddressBox.Text);
            Dispatcher.BeginInvoke(() => Browser.Focus(), DispatcherPriority.Background);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _isEditingAddress = false;
            AddressBox.Text = Browser.Source?.ToString() ?? AddressBox.Text;
            Dispatcher.BeginInvoke(() => Browser.Focus(), DispatcherPriority.Background);
        }
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _isEditingAddress = true;
        AddressBox.SelectAll();
    }

    private void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
    }

    private void AddressBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _isEditingAddress = true;
        AddressBox.SelectAll();
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (AddressBox.IsKeyboardFocusWithin && e.Key == Key.Escape)
        {
            return;
        }

        if (HandleShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void Browser_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleShortcut(e.Key, CurrentModifierKeys()))
        {
            e.Handled = true;
        }
    }

    private IntPtr WindowKeyboardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmKeyDown = 0x0100;
        const int wmSysKeyDown = 0x0104;
        if (msg is not wmKeyDown and not wmSysKeyDown)
        {
            return IntPtr.Zero;
        }

        var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
        if (AddressBox.IsKeyboardFocusWithin && key == Key.Escape)
        {
            return IntPtr.Zero;
        }

        if (HandleShortcut(key, CurrentModifierKeys()))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.L)
        {
            ShowChromeAndFocusAddress();
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.L)
        {
            ShowChromeAndFocusAddress();
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.H)
        {
            Navigate(_settings.HomeUrl);
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.W)
        {
            CloseThisWindow();
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.T)
        {
            OpenNewWindowFromCurrentPage();
            return true;
        }

        if (key == Key.F5 || (modifiers == ModifierKeys.Control && key == Key.R))
        {
            Browser.Reload();
            return true;
        }

        if (modifiers == ModifierKeys.Alt && key == Key.Left)
        {
            if (Browser.CanGoBack)
            {
                Browser.GoBack();
            }

            return true;
        }

        if (modifiers == ModifierKeys.Alt && key == Key.Right)
        {
            if (Browser.CanGoForward)
            {
                Browser.GoForward();
            }

            return true;
        }

        if (key == Key.F8)
        {
            ToggleChrome();
            return true;
        }

        if (key == Key.F9 || (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F))
        {
            ToggleBorderMode();
            return true;
        }

        if (key == Key.Escape && _profile.ChromeVisible)
        {
            ToggleChrome();
            return true;
        }

        return false;
    }

    private static ModifierKeys CurrentModifierKeys()
    {
        var modifiers = Keyboard.Modifiers;
        if (IsKeyDown(0x10))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsKeyDown(0x11))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsKeyDown(0x12))
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x8000) != 0;
    }

    private void QuickSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string url })
        {
            Navigate(url);
        }
    }

    private void ChromeDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void ExitApplication()
    {
        _isReallyClosing = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void CloseThisWindow()
    {
        _removeProfileOnClose = true;
        ((App)System.Windows.Application.Current).RemoveProfile(_profile);
        _isReallyClosing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            _edgeAutoHideService.Reveal();
            SaveSettings();
            e.Cancel = true;
            Hide();
            return;
        }

        if (!_removeProfileOnClose)
        {
            SaveSettings();
        }

        _trayService.Dispose();
        _hotkeyService?.Dispose();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _edgeAutoHideService.Dispose();
        _keyboardSource?.RemoveHook(WindowKeyboardHook);
        _trayService.Dispose();
        _hotkeyService?.Dispose();
        base.OnClosed(e);
    }

    private void SaveSettings()
    {
        if (_edgeAutoHideService.IsHidden)
        {
            return;
        }

        if (!_applyingSiteProfile)
        {
            _profile.Width = Width;
            _profile.Height = Height;
            _profile.Opacity = Opacity;
        }

        _profile.Left = Left;
        _profile.Top = Top;
        _profile.Url = Browser.Source?.ToString() ?? _profile.Url;
        _settings.LastUrl = _profile.Url;
        _settingsService.Save(_settings);
    }

    private bool IsAdBlockEnabledForUrl(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return _settings.AdBlockEnabled && _profile.AdBlockEnabled;
        }

        return _settings.AdBlockEnabled &&
               (SiteProfileForHost(uri.Host)?.AdBlockEnabled ?? _profile.AdBlockEnabled);
    }

    private bool IsCurrentSiteAdBlockEnabled()
    {
        var host = CurrentHost();
        return _settings.AdBlockEnabled &&
               (SiteProfileForHost(host)?.AdBlockEnabled ?? _profile.AdBlockEnabled) &&
               (string.IsNullOrWhiteSpace(host) || !_settings.AdBlockWhitelist.Any(item => HostMatches(host, item)));
    }

    private string CurrentHost()
    {
        var rawUrl = Browser.Source?.ToString();
        return Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }

    private static bool HostMatches(string host, string candidate)
    {
        var trimmed = candidate.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.Host;
        }

        trimmed = trimmed.TrimStart('.').TrimEnd('/');
        return host.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private SiteProfile? CurrentSiteProfile()
    {
        return SiteProfileForHost(CurrentHost());
    }

    private SiteProfile? SiteProfileForHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return _settings.SiteProfiles.FirstOrDefault(site => HostMatches(host, site.Host));
    }

    private SiteProfile GetOrCreateSiteProfile(string host)
    {
        var normalized = NormalizeHost(host);
        var existing = _settings.SiteProfiles.FirstOrDefault(site => HostMatches(normalized, site.Host));
        if (existing is not null)
        {
            return existing;
        }

        var profile = new SiteProfile { Host = normalized };
        _settings.SiteProfiles.Add(profile);
        return profile;
    }

    private void SaveCurrentSiteProfile()
    {
        var host = CurrentHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        var site = GetOrCreateSiteProfile(host);
        site.MobileMode = _profile.MobileMode;
        site.AdBlockEnabled = IsCurrentSiteAdBlockEnabled();
        site.Topmost = Topmost;
        site.ChromeVisible = _profile.ChromeVisible;
        site.Borderless = _profile.Borderless;
        site.Opacity = Opacity;
        site.SizePresetIndex = _profile.SizePresetIndex;
        SaveSettings();
        StatusText.Text = $"Saved profile for {site.Host}";
    }

    private void ClearCurrentSiteProfile()
    {
        var host = CurrentHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        _settings.SiteProfiles.RemoveAll(site => HostMatches(host, site.Host));
        SaveSettings();
        StatusText.Text = $"Cleared profile for {host}";
    }

    private void ApplySiteProfileForUrl(string rawUrl, bool saveWindowState)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var site = SiteProfileForHost(uri.Host);
        if (site is null)
        {
            return;
        }

        _applyingSiteProfile = true;
        try
        {
            _profile.MobileMode = site.MobileMode;
            _profile.AdBlockEnabled = site.AdBlockEnabled;
            _profile.Topmost = site.Topmost;
            _profile.ChromeVisible = site.ChromeVisible;
            _profile.Borderless = site.Borderless;
            _profile.Opacity = site.Opacity;
            _profile.SizePresetIndex = site.SizePresetIndex;
            Topmost = site.Topmost;
            Opacity = ClampOpacity(site.Opacity);
            ApplyWindowPreset(CurrentPreset());
            ApplyBorderMode();
            ApplyChromeVisibility();
            ApplyUserAgent();
            UpdateToggleLabels();
        }
        finally
        {
            _applyingSiteProfile = false;
        }

        if (saveWindowState)
        {
            SaveSettings();
        }
    }

    private static string NormalizeHost(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.Host;
        }

        return trimmed.TrimStart('.').TrimEnd('/');
    }

    private WindowPreset CurrentPreset()
    {
        if (_profile.SizePresetIndex < 0 || _profile.SizePresetIndex >= SizePresets.Length)
        {
            _profile.SizePresetIndex = 0;
        }

        return SizePresets[_profile.SizePresetIndex];
    }

    private void ApplyWindowPreset(WindowPreset preset)
    {
        Width = preset.Width;
        Height = preset.Height;
    }

    private void ToggleChrome()
    {
        _profile.ChromeVisible = !_profile.ChromeVisible;
        ApplyChromeVisibility();
        SaveSettings();
        UpdateToggleLabels();
    }

    private void ToggleBorderMode()
    {
        _edgeAutoHideService.Reveal();
        _profile.Borderless = !_profile.Borderless;
        if (!_profile.Borderless)
        {
            _profile.ChromeVisible = true;
        }

        ApplyBorderMode();
        ApplyChromeVisibility();
        SaveSettings();
        UpdateToggleLabels();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ResetLayout()
    {
        _edgeAutoHideService.Reveal();
        _profile.Borderless = false;
        _profile.ChromeVisible = true;
        _profile.SizePresetIndex = 0;
        _profile.Opacity = 1.0;
        Opacity = 1.0;
        ApplyWindowPreset(CurrentPreset());
        PositionPopup(_trayService.GetTrayAnchorPoint());
        ApplyBorderMode();
        ApplyChromeVisibility();
        SaveSettings();
        UpdateToggleLabels();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowChromeAndFocusAddress()
    {
        ShowChrome();
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void ShowChrome()
    {
        if (_profile.ChromeVisible)
        {
            ApplyChromeVisibility();
            UpdateToggleLabels();
            return;
        }

        _profile.ChromeVisible = true;
        ApplyChromeVisibility();
        SaveSettings();
        UpdateToggleLabels();
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T matched)
            {
                return matched;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible && IsActive)
        {
            Hide();
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowWindowAndFocusAddress()
    {
        if (IsVisible)
        {
            _edgeAutoHideService.Reveal();
            SaveSettings();
            Hide();
            return;
        }

        _edgeAutoHideService.Reveal();
        ShowChrome();
        Show();
        WindowState = WindowState.Normal;
        PositionPopup(_trayService.GetTrayAnchorPoint());
        BringWindowToForeground();
        FocusAddressBar();
        Topmost = true;
        Topmost = _profile.Topmost;
        Dispatcher.BeginInvoke(() =>
        {
            PositionPopup(_trayService.GetTrayAnchorPoint());
            BringWindowToForeground();
            FocusAddressBar();
        }, DispatcherPriority.ApplicationIdle);
        Dispatcher.BeginInvoke(FocusAddressBar, DispatcherPriority.ContextIdle);
    }

    private void BringWindowToForeground()
    {
        Focus();
        Activate();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        SetFocus(handle);
    }

    private void FocusAddressBar()
    {
        _isEditingAddress = true;
        FocusManager.SetFocusedElement(this, AddressBox);
        AddressBox.Focus();
        Keyboard.Focus(AddressBox);
        AddressBox.SelectAll();
    }

    private void ShowAboveTray(System.Drawing.Point trayPoint)
    {
        if (IsVisible)
        {
            _edgeAutoHideService.Reveal();
            SaveSettings();
            Hide();
            return;
        }

        _edgeAutoHideService.Reveal();
        ShowChrome();
        Show();
        WindowState = WindowState.Normal;
        PositionPopup(trayPoint);
        BringWindowToForeground();
        SaveSettings();
    }

    private void PositionPopup(System.Drawing.Point trayPoint)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(trayPoint);
        var work = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(work.Left, work.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(work.Right, work.Bottom));
        PositionPopupInBounds(topLeft, bottomRight);
    }

    private void PositionPopupInWorkArea(Rect work)
    {
        PositionPopupInBounds(
            new System.Windows.Point(work.Left, work.Top),
            new System.Windows.Point(work.Right, work.Bottom));
    }

    private void PositionPopupInBounds(System.Windows.Point topLeft, System.Windows.Point bottomRight)
    {
        _edgeAutoHideService.Reveal();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var margin = 8d;
        var targetLeft = _settings.PopupPosition switch
        {
            "BottomLeft" => topLeft.X + margin,
            "BottomCenter" => topLeft.X + ((bottomRight.X - topLeft.X) - width) / 2,
            _ => bottomRight.X - width - margin
        };
        var targetTop = bottomRight.Y - height - margin;

        Left = Math.Clamp(targetLeft, topLeft.X + margin, bottomRight.X - width - margin);
        Top = Math.Clamp(targetTop, topLeft.Y + margin, bottomRight.Y - height - margin);
    }

    private void OpenNewWindowFromCurrentPage()
    {
        ((App)System.Windows.Application.Current).OpenWindow(new WindowProfile
        {
            Url = Browser.Source?.ToString() ?? _settings.HomeUrl,
            Width = Width,
            Height = Height,
            MobileMode = _profile.MobileMode,
            Topmost = Topmost,
            Borderless = _profile.Borderless,
            ChromeVisible = _profile.ChromeVisible,
            AdBlockEnabled = _profile.AdBlockEnabled,
            SizePresetIndex = _profile.SizePresetIndex
        });
    }

    private static double ClampOpacity(double value)
    {
        if (double.IsNaN(value))
        {
            return 1.0;
        }

        return Math.Clamp(value, 0.7, 1.0);
    }

    private sealed record WindowPreset(string Name, double Width, double Height);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
