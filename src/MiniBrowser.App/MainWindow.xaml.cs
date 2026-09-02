using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class MainWindow : Window
{
    private static readonly WindowPreset[] SizePresets =
    [
        new("390x844", 390, 844),
        new("430x932", 430, 932),
        new("360x780", 360, 780),
        new("768x920", 768, 920)
    ];

    private static readonly double[] OpacityPresets = [1.0, 0.92, 0.84, 0.76];
    private readonly SettingsService _settingsService;
    private readonly AdBlockService _adBlockService;
    private readonly EdgeAutoHideService _edgeAutoHideService;
    private readonly string _cosmeticScript;
    private readonly AppSettings _settings;
    private readonly WindowProfile _profile;
    private readonly TabSessionService _tabSession;
    private readonly TrayService _trayService;
    private readonly HotkeyService? _hotkeyService;
    private readonly bool _isPrimaryWindow;
    private readonly List<BrowserTab> _tabs = [];
    private CoreWebView2Environment? _browserEnvironment;
    private BrowserTab? _activeTab;
    private bool _hotkeyWarningShown;
    private bool _isReallyClosing;
    private bool _isEditingAddress;
    private bool _applyingSiteProfile;
    private bool _usesPopupStartupPosition;
    private bool _browserSuspended;
    private bool _suspendInProgress;
    private bool _suspendRequested;
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
        _tabSession = new TabSessionService(profile, settings.HomeUrl);
        _isPrimaryWindow = enableHotkey;
        _adBlockService = adBlockService;
        _cosmeticScript = cosmeticScript;
        _trayService = new TrayService(this, ExitApplication, ToggleBorderMode, ShowChrome, ShowAboveTray);
        _edgeAutoHideService = new EdgeAutoHideService(
            this,
            () => _settings.EdgeAutoHideEnabled,
            hidden: null,
            ResumeBrowserIfNeeded);

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
        MouseEnter += (_, _) => _edgeAutoHideService.Reveal();
        MouseMove += (_, _) => _edgeAutoHideService.Reveal();
        SourceInitialized += MainWindow_SourceInitialized;
        SourceInitialized += (_, _) => _edgeAutoHideService.Start();
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
        IsVisibleChanged += MainWindow_IsVisibleChanged;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _keyboardSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
        _keyboardSource?.AddHook(WindowKeyboardHook);
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ResumeBrowserIfNeeded();
        }
        else
        {
            SuspendBrowserWhenHidden();
        }
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

            _browserEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: RuntimePaths.WebView2DataDirectory,
                options: null);
            await InitializeTabsAsync();
            if (_settings.CompatibilityCacheResetPending && await ClearRuntimeCacheAsync())
            {
                _settings.CompatibilityCacheResetPending = false;
                SaveSettings();
            }
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

    private async Task InitializeTabsAsync()
    {
        if (_browserEnvironment is null)
        {
            return;
        }

        foreach (var tabProfile in _profile.Tabs)
        {
            _tabs.Add(new BrowserTab(tabProfile));
        }

        var active = _tabs.FirstOrDefault(tab => tab.Profile.Id == _profile.ActiveTabId) ?? _tabs[0];
        active.Browser = Browser;
        AttachBrowserKeyboardHandlers(Browser);
        _activeTab = active;
        _profile.ActiveTabId = active.Profile.Id;
        BrowserHost.Content = Browser;
        await InitializeBrowserAsync(active, navigate: true);
        RenderTabs();
    }

    private async Task InitializeBrowserAsync(BrowserTab tab, bool navigate)
    {
        if (_browserEnvironment is null)
        {
            return;
        }

        if (tab.Browser is null)
        {
            tab.Browser = new WebView2();
            AttachBrowserKeyboardHandlers(tab.Browser);
        }

        if (!tab.IsInitialized)
        {
            await tab.Browser.EnsureCoreWebView2Async(_browserEnvironment);
            await ConfigureBrowserAsync(tab.Browser, tab);
            tab.IsInitialized = true;
        }

        if (navigate && !IsPersistableUrl(tab.Browser.Source?.ToString()))
        {
            NavigateTab(tab, tab.Profile.Url);
        }
    }

    private void AttachBrowserKeyboardHandlers(WebView2 browser)
    {
        browser.PreviewKeyDown -= Browser_KeyDown;
        browser.KeyDown -= Browser_KeyDown;
        browser.PreviewKeyDown += Browser_KeyDown;
        browser.KeyDown += Browser_KeyDown;
    }

    private async Task CreateNewTabAsync(string? rawUrl = null, bool activate = true)
    {
        if (_tabSession.Tabs.Count >= TabSessionService.MaximumTabs)
        {
            StatusText.Text = $"Tab limit reached ({TabSessionService.MaximumTabs})";
            return;
        }

        var url = string.IsNullOrWhiteSpace(rawUrl) ? _settings.HomeUrl : NormalizeUrl(rawUrl);
        var profile = _tabSession.Create(url);
        var tab = new BrowserTab(profile);
        _profile.Tabs.Add(profile);
        _tabs.Add(tab);
        RenderTabs();
        if (activate)
        {
            HideTabOverview();
            await ActivateTabAsync(tab);
            FocusAddressBar();
        }

        SaveSettings();
    }

    private async Task ActivateTabAsync(BrowserTab tab)
    {
        if (ReferenceEquals(tab, _activeTab) && tab.IsInitialized)
        {
            return;
        }

        var previous = _activeTab;
        if (previous is not null && previous.Browser is not null)
        {
            var previousUrl = previous.Browser.Source?.ToString();
            if (IsPersistableUrl(previousUrl))
            {
                previous.Profile.Url = previousUrl!;
            }
            await CaptureTabPreviewAsync(previous);
            ScheduleSuspendTab(previous);
        }

        _activeTab = tab;
        _tabSession.Activate(tab.Profile.Id);
        if (tab.Browser is null)
        {
            tab.Browser = new WebView2();
            AttachBrowserKeyboardHandlers(tab.Browser);
        }

        BrowserHost.Content = null;
        Browser = tab.Browser;
        BrowserHost.Content = Browser;
        await InitializeBrowserAsync(tab, navigate: true);
        CancelPendingSuspend(tab);
        ResumeTab(tab);
        AddressBox.Text = tab.Browser.Source?.ToString() ?? tab.Profile.Url;
        _profile.Url = tab.Profile.Url;
        _settings.LastUrl = tab.Profile.Url;
        ApplySiteProfileForUrl(tab.Profile.Url, saveWindowState: false);
        UpdateNavigationButtons();
        RenderTabs();
        SaveSettings();
    }

    private static async Task SuspendTabAsync(BrowserTab tab)
    {
        if (!tab.IsInitialized || tab.IsSuspended || tab.Browser?.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            tab.IsSuspended = await tab.Browser.CoreWebView2.TrySuspendAsync();
        }
        catch (InvalidOperationException)
        {
            tab.IsSuspended = false;
        }
    }

    private void ScheduleSuspendTab(BrowserTab tab)
    {
        CancelPendingSuspend(tab);
        tab.SuspendCancellation = new CancellationTokenSource();
        _ = SuspendTabAfterDelayAsync(tab, tab.SuspendCancellation.Token);
    }

    private static async Task SuspendTabAfterDelayAsync(BrowserTab tab, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            var core = tab.Browser?.CoreWebView2;
            if (core is null ||
                !TabSuspensionPolicy.CanSuspend(tab.TopLevelHost, core.IsDocumentPlayingAudio, tab.ActiveDownloads))
            {
                return;
            }

            await SuspendTabAsync(tab);
        }
        catch (OperationCanceledException)
        {
            // Returning to a tab cancels its pending background suspension.
        }
    }

    private static void CancelPendingSuspend(BrowserTab tab)
    {
        tab.SuspendCancellation?.Cancel();
        tab.SuspendCancellation?.Dispose();
        tab.SuspendCancellation = null;
    }

    private static void ResumeTab(BrowserTab tab)
    {
        if (!tab.IsSuspended || tab.Browser?.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            tab.Browser.CoreWebView2.Resume();
        }
        catch (InvalidOperationException)
        {
            // The tab may already have resumed while it was being activated.
        }

        tab.IsSuspended = false;
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var closeResult = _tabSession.Close(tab.Profile.Id);
        if (_tabs.Count == 1)
        {
            var replacement = new BrowserTab(closeResult.Active);
            _tabs.Add(replacement);
            await ActivateTabAsync(replacement);
        }
        else if (ReferenceEquals(tab, _activeTab))
        {
            var replacement = _tabs.First(item => item.Profile.Id == closeResult.Active.Id);
            await ActivateTabAsync(replacement);
        }

        _tabs.Remove(tab);
        CancelPendingSuspend(tab);
        tab.Browser?.Dispose();
        RenderTabs();
        SaveSettings();
    }

    private void RenderTabs()
    {
        TabCountText.Text = _tabs.Count > 99 ? "99+" : _tabs.Count.ToString();
        TabOverviewButton.ToolTip = $"Tabs ({_tabs.Count})";
        if (TabOverviewOverlay.Visibility == Visibility.Visible)
        {
            RenderTabOverview();
        }
    }

    private async void TabOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (TabOverviewOverlay.Visibility == Visibility.Visible)
        {
            HideTabOverview();
            return;
        }

        await ShowTabOverviewAsync();
    }

    private async void OverviewNewTabButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateNewTabAsync();
    }

    private void CloseTabOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        HideTabOverview();
    }

    private async Task ShowTabOverviewAsync()
    {
        if (_activeTab is not null)
        {
            await CaptureTabPreviewAsync(_activeTab);
        }

        BrowserHost.Visibility = Visibility.Collapsed;
        TabOverviewOverlay.Visibility = Visibility.Visible;
        TabOverviewTitle.Text = _tabs.Count == 1 ? "1 tab" : $"{_tabs.Count} tabs";
        RenderTabOverview();
    }

    private void HideTabOverview()
    {
        TabOverviewOverlay.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
        Browser.Focus();
    }

    private void RenderTabOverview()
    {
        TabOverviewTitle.Text = _tabs.Count == 1 ? "1 tab" : $"{_tabs.Count} tabs";
        TabCardsPanel.Children.Clear();
        var cardWidth = Math.Clamp((ActualWidth - 42) / 2, 132, 184);
        foreach (var tab in _tabs)
        {
            var card = CreateTabCard(tab, cardWidth);
            TabCardsPanel.Children.Add(card);
        }
    }

    private Border CreateTabCard(BrowserTab tab, double width)
    {
        var preview = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 240, 244)) };
        if (tab.Preview is not null)
        {
            preview.Children.Add(new System.Windows.Controls.Image { Source = tab.Preview, Stretch = System.Windows.Media.Stretch.UniformToFill });
        }
        else
        {
            preview.Children.Add(new TextBlock
            {
                Text = "\uE774",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 28,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 156, 169)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
        }

        var close = new System.Windows.Controls.Button
        {
            Content = "\uE711",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            ToolTip = "Close tab"
        };
        close.Click += async (_, args) =>
        {
            args.Handled = true;
            await CloseTabAsync(tab);
            if (TabOverviewOverlay.Visibility == Visibility.Visible)
            {
                RenderTabOverview();
            }
        };
        preview.Children.Add(close);

        var title = new TextBlock
        {
            Text = tab.Profile.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var host = new TextBlock
        {
            Text = HostFromUrl(tab.Profile.Url),
            FontSize = 10,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var details = new StackPanel { Margin = new Thickness(10, 9, 10, 10) };
        details.Children.Add(title);
        details.Children.Add(host);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(142) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(preview);
        Grid.SetRow(details, 1);
        layout.Children.Add(details);

        var card = new Border
        {
            Width = width,
            MinHeight = 198,
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(6),
            BorderThickness = ReferenceEquals(tab, _activeTab) ? new Thickness(1.5) : new Thickness(1),
            BorderBrush = ReferenceEquals(tab, _activeTab)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 222, 229)),
            Background = System.Windows.Media.Brushes.White,
            ClipToBounds = true,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = layout,
            ToolTip = tab.Profile.Url
        };
        card.MouseLeftButtonUp += async (_, args) =>
        {
            if (args.Handled)
            {
                return;
            }

            await ActivateTabAsync(tab);
            HideTabOverview();
        };
        return card;
    }

    private static async Task CaptureTabPreviewAsync(BrowserTab tab)
    {
        if (!tab.IsInitialized || tab.Browser?.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream();
            await tab.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Position = 0;
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            tab.Preview = image;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to capture tab preview.");
        }
    }

    private static string NormalizeTabTitle(string? title, string url)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "New tab";
    }

    private static bool IsPersistableUrl(string? rawUrl)
    {
        return Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private void NavigateTab(BrowserTab tab, string rawUrl)
    {
        if (tab.Browser is null)
        {
            return;
        }

        var url = NormalizeUrl(rawUrl);
        tab.Profile.Url = url;
        if (ReferenceEquals(tab, _activeTab))
        {
            AddressBox.Text = url;
            ApplySiteProfileForUrl(url, saveWindowState: false);
        }

        tab.Browser.Source = new Uri(url);
    }

    private async Task ConfigureBrowserAsync(WebView2 browser, BrowserTab tab)
    {
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        browser.CoreWebView2.Settings.UserAgent = string.Empty;
        if (!string.IsNullOrWhiteSpace(_cosmeticScript))
        {
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_cosmeticScript);
        }

        AddAdBlockRequestFilters(browser);
        browser.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (!string.IsNullOrWhiteSpace(args.Uri))
            {
                Dispatcher.BeginInvoke(() => _ = CreateNewTabAsync(args.Uri, activate: true));
            }
        };
        browser.CoreWebView2.DownloadStarting += (_, args) =>
        {
            tab.ActiveDownloads++;
            var operation = args.DownloadOperation;
            EventHandler<object>? stateChanged = null;
            stateChanged = (_, _) =>
            {
                if (operation.State == CoreWebView2DownloadState.InProgress)
                {
                    return;
                }

                tab.ActiveDownloads = Math.Max(0, tab.ActiveDownloads - 1);
                operation.StateChanged -= stateChanged;
            };
            operation.StateChanged += stateChanged;
        };
        browser.CoreWebView2.WebResourceRequested += (_, args) => Browser_WebResourceRequested(tab, browser, args);
        browser.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!IsPersistableUrl(args.Uri))
            {
                return;
            }

            tab.TopLevelHost = HostFromUrl(args.Uri);
            tab.BlockedRequestCount = 0;
            tab.LastBlockedRule = string.Empty;
            tab.Profile.Url = args.Uri;
            if (ReferenceEquals(_activeTab, tab) && !_isEditingAddress)
            {
                AddressBox.Text = args.Uri;
            }

            if (ReferenceEquals(_activeTab, tab))
            {
                StatusText.Text = "Loading...";
                UpdateNavigationButtons();
            }
        };
        browser.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            var currentUrl = browser.Source?.ToString();
            if (IsPersistableUrl(currentUrl))
            {
                tab.Profile.Url = currentUrl!;
            }
            tab.Profile.Title = NormalizeTabTitle(browser.CoreWebView2.DocumentTitle, tab.Profile.Url);
            if (ReferenceEquals(_activeTab, tab))
            {
                StatusText.Text = args.IsSuccess ? "Ready" : $"Load failed: {args.WebErrorStatus}";
                if (!_isEditingAddress)
                {
                    AddressBox.Text = tab.Profile.Url;
                }

                if (args.IsSuccess)
                {
                    _profile.Url = tab.Profile.Url;
                    _settings.LastUrl = tab.Profile.Url;
                    ApplySiteProfileForUrl(tab.Profile.Url, saveWindowState: false);
                }

                UpdateNavigationButtons();
            }

            RenderTabs();
            SaveSettings();
        };
        browser.CoreWebView2.SourceChanged += (_, _) =>
        {
            var currentUrl = browser.Source?.ToString();
            if (!IsPersistableUrl(currentUrl))
            {
                return;
            }

            tab.Profile.Url = currentUrl!;
            if (ReferenceEquals(_activeTab, tab) && !_isEditingAddress)
            {
                AddressBox.Text = tab.Profile.Url;
            }

            if (ReferenceEquals(_activeTab, tab))
            {
                UpdateNavigationButtons();
            }
        };
    }

    private static void AddAdBlockRequestFilters(WebView2 browser)
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
            browser.CoreWebView2.AddWebResourceRequestedFilter("*", context);
        }
    }

    private void Browser_WebResourceRequested(BrowserTab tab, WebView2 browser, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var enabled = IsAdBlockEnabledForUrl(tab, e.Request.Uri);
        var decision = _adBlockService.Evaluate(e.Request.Uri, enabled, _settings.AdBlockWhitelist);
        if (!decision.IsBlocked)
        {
            return;
        }

        tab.BlockedRequestCount++;
        tab.LastBlockedRule = string.IsNullOrWhiteSpace(decision.Rule)
            ? decision.Reason
            : $"{decision.Reason}: {decision.Rule}";
        e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream([]),
            204,
            "Blocked",
            "Content-Type: text/plain");
    }

    private async void SuspendBrowserWhenHidden()
    {
        _suspendRequested = true;
        if (_isReallyClosing || _browserSuspended || _suspendInProgress || Browser.CoreWebView2 is null)
        {
            return;
        }

        _suspendInProgress = true;
        try
        {
            _browserSuspended = await Browser.CoreWebView2.TrySuspendAsync();
            if (_browserSuspended && !_suspendRequested)
            {
                Browser.CoreWebView2.Resume();
                _browserSuspended = false;
            }
        }
        catch (Exception ex) when (IsDisposedWebViewException(ex))
        {
            _browserSuspended = false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to suspend WebView2.");
        }
        finally
        {
            _suspendInProgress = false;
        }
    }

    private void ResumeBrowserIfNeeded()
    {
        _suspendRequested = false;
        if (_isReallyClosing || !_browserSuspended || Browser.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            Browser.CoreWebView2.Resume();
            _browserSuspended = false;
        }
        catch (Exception ex) when (IsDisposedWebViewException(ex))
        {
            _browserSuspended = false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to resume WebView2.");
        }
    }

    private static bool IsDisposedWebViewException(Exception ex)
    {
        return ex is InvalidOperationException &&
               ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase);
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
        return NavigationService.Resolve(raw, _settings.HomeUrl, _settings.SearchEngineUrl);
    }

    private void ApplyUserAgent()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        // Keep WebView2's native Windows UA. The compact window already triggers responsive layouts,
        // while a forged Safari/iPhone UA creates a platform mismatch that bot checks can flag.
        Browser.CoreWebView2.Settings.UserAgent = string.Empty;
    }

    private void UpdateToggleLabels()
    {
        MenuButton.Content = "\uE700";
        RenderTabs();
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

        var newTab = new MenuItem { Header = "New tab    Ctrl+T" };
        newTab.Click += (_, _) => _ = CreateNewTabAsync();
        menu.Items.Add(newTab);

        menu.Items.Add(new Separator());
        var blockedCount = _activeTab?.BlockedRequestCount ?? 0;
        var shield = new MenuItem
        {
            Header = IsCurrentSiteAdBlockEnabled()
                ? $"Ad block: ON · {blockedCount} blocked"
                : "Ad block: OFF for this site",
            ToolTip = string.IsNullOrWhiteSpace(_activeTab?.LastBlockedRule)
                ? "No blocked request on this page"
                : $"Last rule: {_activeTab.LastBlockedRule}"
        };
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

        var closeTab = new MenuItem { Header = "Close tab    Ctrl+W" };
        closeTab.Click += (_, _) =>
        {
            if (_activeTab is not null)
            {
                _ = CloseTabAsync(_activeTab);
            }
        };
        menu.Items.Add(closeTab);

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

    private async Task<bool> ClearRuntimeCacheAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return false;
        }

        try
        {
            StatusText.Text = "Clearing cache...";
            await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache |
                CoreWebView2BrowsingDataKinds.CacheStorage |
                CoreWebView2BrowsingDataKinds.ServiceWorkers);
            StatusText.Text = "Cache cleared";
            return true;
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
            return false;
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
        if (key == Key.Escape && TabOverviewOverlay.Visibility == Visibility.Visible)
        {
            HideTabOverview();
            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            ActivateRelativeTab(1);
            return true;
        }

        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ActivateRelativeTab(-1);
            return true;
        }

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
            if (_activeTab is not null)
            {
                _ = CloseTabAsync(_activeTab);
            }
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.T)
        {
            _ = CreateNewTabAsync();
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

    private void ActivateRelativeTab(int offset)
    {
        if (_activeTab is null || _tabs.Count < 2)
        {
            return;
        }

        var profile = _tabSession.ActivateRelative(offset);
        var next = _tabs.First(tab => tab.Profile.Id == profile.Id);
        _ = ActivateTabAsync(next);
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

        SaveSettings();

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
        foreach (var tab in _tabs)
        {
            CancelPendingSuspend(tab);
            tab.Browser?.Dispose();
        }
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
        if (_activeTab is not null)
        {
            var currentUrl = Browser.Source?.ToString();
            if (IsPersistableUrl(currentUrl))
            {
                _activeTab.Profile.Url = currentUrl!;
            }
            _profile.ActiveTabId = _activeTab.Profile.Id;
            _profile.Url = _activeTab.Profile.Url;
        }
        _settings.LastUrl = _profile.Url;
        _settingsService.Save(_settings);
    }

    private bool IsAdBlockEnabledForUrl(BrowserTab tab, string rawUrl)
    {
        var currentHost = HostFromUrl(tab.Browser?.Source?.ToString() ?? string.Empty);
        var topLevelHost = string.IsNullOrWhiteSpace(tab.TopLevelHost) ? currentHost : tab.TopLevelHost;
        if (BrowserCompatibilityPolicy.BypassAdBlockForRequest(topLevelHost, currentHost))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return _settings.AdBlockEnabled && _profile.AdBlockEnabled;
        }

        if (BrowserCompatibilityPolicy.BypassAdBlockForRequest(topLevelHost, uri.Host))
        {
            return false;
        }

        return _settings.AdBlockEnabled &&
               (SiteProfileForHost(topLevelHost)?.AdBlockEnabled ?? _profile.AdBlockEnabled);
    }

    private bool IsCurrentSiteAdBlockEnabled()
    {
        var host = CurrentHost();
        if (BrowserCompatibilityPolicy.BypassAdBlockForHost(host))
        {
            return false;
        }

        return _settings.AdBlockEnabled &&
               (SiteProfileForHost(host)?.AdBlockEnabled ?? _profile.AdBlockEnabled) &&
               (string.IsNullOrWhiteSpace(host) || !_settings.AdBlockWhitelist.Any(item => HostMatches(host, item)));
    }

    private string CurrentHost()
    {
        var rawUrl = Browser.Source?.ToString();
        return Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }

    private static string HostFromUrl(string rawUrl)
    {
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

    public void ShowFromExternalActivation()
    {
        _edgeAutoHideService.Reveal();
        ShowChrome();
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            PositionPopup(_trayService.GetTrayAnchorPoint());
        }

        BringWindowToForeground();
        FocusAddressBar();
        Dispatcher.BeginInvoke(() =>
        {
            BringWindowToForeground();
            FocusAddressBar();
        }, DispatcherPriority.ApplicationIdle);
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
        var bounds = WindowPlacementService.Calculate(
            topLeft.X,
            topLeft.Y,
            bottomRight.X,
            bottomRight.Y,
            width,
            height,
            _settings.PopupPosition);
        Left = bounds.Left;
        Top = bounds.Top;
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

    private sealed class BrowserTab(TabProfile profile)
    {
        public TabProfile Profile { get; } = profile;
        public WebView2? Browser { get; set; }
        public string TopLevelHost { get; set; } = string.Empty;
        public bool IsInitialized { get; set; }
        public bool IsSuspended { get; set; }
        public int ActiveDownloads { get; set; }
        public int BlockedRequestCount { get; set; }
        public string LastBlockedRule { get; set; } = string.Empty;
        public CancellationTokenSource? SuspendCancellation { get; set; }
        public System.Windows.Media.ImageSource? Preview { get; set; }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
