using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class MainWindow : Window
{
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
    private readonly UpdateService _updateService = new();
    private readonly AppSettings _settings;
    private readonly WindowProfile _profile;
    private readonly TrayService _trayService;
    private readonly HotkeyService? _hotkeyService;
    private readonly bool _isPrimaryWindow;
    private bool _hotkeyWarningShown;
    private bool _isReallyClosing;
    private bool _isEditingAddress;
    private bool _removeProfileOnClose;
    private int _blockedRequestCount;

    public MainWindow(SettingsService settingsService, AppSettings settings, WindowProfile profile, bool enableHotkey)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = settings;
        _profile = profile;
        _isPrimaryWindow = enableHotkey;
        _adBlockService = new AdBlockService(_settings.CustomBlockedHosts);
        _adBlockService.LoadEasyListLite(RuntimePaths.EasyListLitePath);
        _trayService = new TrayService(this, ExitApplication, ToggleBorderMode, ShowChrome, ShowAboveTray);

        Width = _profile.Width;
        Height = _profile.Height;
        if (_profile.Left >= 0 && _profile.Top >= 0)
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
            _hotkeyService.Pressed += (_, _) => ToggleWindowVisibility();
        }

        UpdateToggleLabels();
        ApplyChromeVisibility();
        ApplyBorderMode();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Show();
            WindowState = WindowState.Normal;
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
            ConfigureBrowser();
            Navigate(string.IsNullOrWhiteSpace(_profile.Url) ? _settings.HomeUrl : _profile.Url);
            _ = CheckForUpdatesOnStartupAsync();
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

    private void ConfigureBrowser()
    {
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        ApplyUserAgent();
        Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_adBlockService.CreateCosmeticScript());
        Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
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
                SaveSettings();
                Browser.CoreWebView2.ExecuteScriptAsync(_adBlockService.CreateCosmeticScript());
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

    private void Browser_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var enabled = _settings.AdBlockEnabled && _profile.AdBlockEnabled;
        if (!_adBlockService.ShouldBlock(e.Request.Uri, enabled, _settings.AdBlockWhitelist))
        {
            return;
        }

        _blockedRequestCount++;
        Dispatcher.Invoke(UpdateToggleLabels);
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
        Browser.Source = new Uri(url);
    }

    private static string NormalizeUrl(string raw)
    {
        var value = raw.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "https://www.bing.com";
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

        return "https://www.bing.com/search?q=" + Uri.EscapeDataString(value);
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

        menu.Items.Add(new Separator());
        foreach (var site in _settings.QuickSites)
        {
            var item = new MenuItem { Header = site.Name, Tag = site.Url };
            item.Click += (_, _) => Navigate(site.Url);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var newWindow = new MenuItem { Header = "New window from this page" };
        newWindow.Click += (_, _) => OpenNewWindowFromCurrentPage();
        menu.Items.Add(newWindow);

        menu.Items.Add(new Separator());
        var phone = new MenuItem { Header = _profile.MobileMode ? "Desktop mode" : "Phone mode" };
        phone.Click += MobileButton_Click;
        menu.Items.Add(phone);

        var pin = new MenuItem { Header = Topmost ? "Unpin window" : "Keep on top" };
        pin.Click += TopmostButton_Click;
        menu.Items.Add(pin);

        var size = new MenuItem { Header = $"Size: {CurrentPreset().Name}" };
        size.Click += SizeButton_Click;
        menu.Items.Add(size);

        var opacity = new MenuItem { Header = $"Opacity: {Math.Round(Opacity * 100)}%" };
        opacity.Click += OpacityButton_Click;
        menu.Items.Add(opacity);

        var frame = new MenuItem { Header = _profile.Borderless ? "Show window frame    F9" : "Hide window frame    F9" };
        frame.Click += BorderButton_Click;
        menu.Items.Add(frame);

        var shield = new MenuItem { Header = IsCurrentSiteAdBlockEnabled() ? "Ad block: ON for this site" : "Ad block: OFF for this site" };
        shield.Click += ShieldButton_Click;
        menu.Items.Add(shield);

        var adStats = new MenuItem
        {
            Header = $"Ad block: {_blockedRequestCount} blocked, {_adBlockService.HostRuleCount} host rules, {_adBlockService.UrlRuleCount} URL rules",
            IsEnabled = false
        };
        menu.Items.Add(adStats);

        var lowMemory = new MenuItem { Header = _settings.LowMemoryMode ? "Low memory mode: ON" : "Low memory mode: OFF" };
        lowMemory.Click += (_, _) => ToggleLowMemoryMode();
        menu.Items.Add(lowMemory);

        var clearCache = new MenuItem { Header = "Clear runtime cache" };
        clearCache.Click += async (_, _) => await ClearRuntimeCacheAsync();
        menu.Items.Add(clearCache);

        var updates = new MenuItem { Header = "Check for updates" };
        updates.Click += async (_, _) => await CheckForUpdatesInteractiveAsync();
        menu.Items.Add(updates);

        var clean = new MenuItem { Header = _profile.ChromeVisible ? "Clean mode    F8" : "Show controls    F8" };
        clean.Click += ChromeButton_Click;
        menu.Items.Add(clean);

        menu.Items.Add(new Separator());
        var resetLayout = new MenuItem { Header = "Reset layout" };
        resetLayout.Click += (_, _) => ResetLayout();
        menu.Items.Add(resetLayout);

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) =>
        {
            var settingsWindow = new SettingsWindow(_settingsService, _settings) { Owner = this };
            settingsWindow.ShowDialog();
            QuickSites.ItemsSource = _settings.QuickSites;
        };
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());
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
        }
        else
        {
            _settings.AdBlockWhitelist.Add(host);
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
            var result = await _updateService.CheckAsync();
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

    private async Task CheckForUpdatesInteractiveAsync()
    {
        try
        {
            StatusText.Text = "Checking updates...";
            var result = await _updateService.CheckAsync();
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
            var zipPath = await _updateService.DownloadAsync(asset, progress);
            var scriptPath = _updateService.PrepareUpdaterScript(zipPath);
            SaveSettings();
            _updateService.LaunchUpdater(scriptPath);
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
            _isEditingAddress = false;
            Navigate(AddressBox.Text);
            Keyboard.ClearFocus();
        }
        else if (e.Key == Key.Escape)
        {
            _isEditingAddress = false;
            AddressBox.Text = Browser.Source?.ToString() ?? AddressBox.Text;
            Keyboard.ClearFocus();
            e.Handled = true;
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

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            ShowChromeAndFocusAddress();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.L)
        {
            ShowChromeAndFocusAddress();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
        {
            Navigate(_settings.HomeUrl);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            CloseThisWindow();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            OpenNewWindowFromCurrentPage();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 || (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R))
        {
            Browser.Reload();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Left)
        {
            if (Browser.CanGoBack)
            {
                Browser.GoBack();
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Right)
        {
            if (Browser.CanGoForward)
            {
                Browser.GoForward();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8)
        {
            ToggleChrome();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9 || (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F))
        {
            ToggleBorderMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _profile.ChromeVisible)
        {
            ToggleChrome();
            e.Handled = true;
        }
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
        _trayService.Dispose();
        _hotkeyService?.Dispose();
        base.OnClosed(e);
    }

    private void SaveSettings()
    {
        _profile.Width = Width;
        _profile.Height = Height;
        _profile.Left = Left;
        _profile.Top = Top;
        _profile.Opacity = Opacity;
        _profile.Url = Browser.Source?.ToString() ?? _profile.Url;
        _settings.LastUrl = _profile.Url;
        _settingsService.Save(_settings);
    }

    private bool IsCurrentSiteAdBlockEnabled()
    {
        var host = CurrentHost();
        return _settings.AdBlockEnabled &&
               _profile.AdBlockEnabled &&
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
        _profile.Borderless = false;
        _profile.ChromeVisible = true;
        _profile.SizePresetIndex = 0;
        _profile.Opacity = 1.0;
        Opacity = 1.0;
        ApplyWindowPreset(CurrentPreset());
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - Height) / 2;
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

    private void ShowAboveTray(System.Drawing.Point trayPoint)
    {
        if (IsVisible)
        {
            SaveSettings();
            Hide();
            return;
        }

        ShowChrome();
        Show();
        WindowState = WindowState.Normal;
        PositionAboveTray(trayPoint);
        Activate();
        SaveSettings();
    }

    private void PositionAboveTray(System.Drawing.Point trayPoint)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(trayPoint);
        var work = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(work.Left, work.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(work.Right, work.Bottom));
        var tray = fromDevice.Transform(new System.Windows.Point(trayPoint.X, trayPoint.Y));

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var margin = 8d;
        var targetLeft = tray.X - width / 2;
        var targetTop = tray.Y - height - margin;

        if (targetTop < topLeft.Y + margin)
        {
            targetTop = tray.Y + margin;
        }

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
}
