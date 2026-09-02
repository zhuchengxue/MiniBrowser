using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;
using System.Text.Json;

var tests = new (string Name, Action Body)[]
{
    ("AdBlock blocks EasyList hosts and URL rules", AdBlockBlocksEasyListRules),
    ("AdBlock honors whitelist", AdBlockHonorsWhitelist),
    ("AdBlock replaces custom hosts without rebuilding service", AdBlockReplacesCustomHostsWithoutRebuildingService),
    ("AdBlock explains the matched rule", AdBlockExplainsMatchedRule),
    ("Cosmetic script includes EasyList selectors", CosmeticScriptIncludesSelectors),
    ("Cosmetic script bypasses verification hosts", CosmeticScriptBypassesVerificationHosts),
    ("Edge auto hide geometry keeps one visible strip", EdgeAutoHideGeometryKeepsOneVisibleStrip),
    ("Edge auto hide reveal only reacts on visible strip", EdgeAutoHideRevealOnlyReactsOnVisibleStrip),
    ("Edge auto hide waits for pointer entry after reveal", EdgeAutoHideWaitsForPointerEntryAfterReveal),
    ("Settings normalizes site profiles", SettingsNormalizesSiteProfiles),
    ("Settings normalizes Google NCR startup URLs", SettingsNormalizesGoogleNcrStartupUrls),
    ("Settings load migrates broken Google startup URL", SettingsLoadMigratesBrokenGoogleStartupUrl),
    ("Settings skips identical repeated saves", SettingsSkipsIdenticalRepeatedSaves),
    ("Settings defaults to Google search", SettingsDefaultsToGoogleSearch),
    ("Settings defaults popup position to bottom right", SettingsDefaultsPopupPositionToBottomRight),
    ("Settings defaults to desktop compatibility mode", SettingsDefaultsToDesktopCompatibilityMode),
    ("Settings migrates legacy low memory mode", SettingsMigratesLegacyLowMemoryMode),
    ("Settings migration disables legacy edge auto hide", SettingsMigrationDisablesLegacyEdgeAutoHide),
    ("Settings merges legacy windows into tabs", SettingsMergesLegacyWindowsIntoTabs),
    ("Settings replaces transient blank tab URLs", SettingsReplacesTransientBlankTabUrls),
    ("Settings caps oversized restored sessions", SettingsCapsOversizedRestoredSessions),
    ("Tabs create and activate a new home tab", TabsCreateAndActivateNewHomeTab),
    ("Tabs close active tab and select its neighbor", TabsCloseActiveTabAndSelectNeighbor),
    ("Tabs close background tab without changing active tab", TabsCloseBackgroundTabWithoutChangingActive),
    ("Tabs replace the final tab with home", TabsReplaceFinalTabWithHome),
    ("Tabs cycle in both directions", TabsCycleInBothDirections),
    ("Tabs repair an invalid active id", TabsRepairInvalidActiveId),
    ("Tabs enforce the session limit", TabsEnforceSessionLimit),
    ("Tab suspension protects audio downloads and authentication", TabSuspensionProtectsSensitiveActivity),
    ("Navigation resolves public hosts with HTTPS", NavigationResolvesPublicHostsWithHttps),
    ("Navigation resolves local development hosts with HTTP", NavigationResolvesLocalHostsWithHttp),
    ("Navigation builds an escaped search URL", NavigationBuildsEscapedSearchUrl),
    ("Navigation rejects invalid search templates", NavigationRejectsInvalidSearchTemplates),
    ("Window placement anchors left center and right", WindowPlacementAnchorsAllPositions),
    ("Window placement clamps oversized windows", WindowPlacementClampsOversizedWindows),
    ("Window placement remains visible at common DPI scales", WindowPlacementRemainsVisibleAtCommonDpiScales),
    ("Single instance signals the primary instance", SingleInstanceSignalsPrimary),
    ("Compatibility policy bypasses verification hosts", CompatibilityPolicyBypassesVerificationHosts),
    ("Settings disables edge auto hide by default", SettingsDisablesEdgeAutoHideByDefault),
    ("Settings disables startup update checks by default", SettingsDisablesStartupUpdateChecksByDefault),
    ("Update parser finds newer portable release", UpdateParserFindsNewerPortableRelease),
    ("Update parser treats current release as current", UpdateParserTreatsCurrentReleaseAsCurrent)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Self-test failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("- " + failure);
    }

    return 1;
}

Console.WriteLine("All self-tests passed.");
return 0;

static AdBlockService CreateAdBlockService()
{
    var listPath = Path.Combine(Path.GetTempPath(), "minibrowser-selftest-easylist.txt");
    File.WriteAllText(
        listPath,
        """
        ! comment
        ||ads.example.com^
        ||tracker.example.net^$third-party
        /adserver/*
        example.com##.sponsored-card
        @@||allowed.example.com^
        """);

    var service = new AdBlockService(["custom-ads.example.org"]);
    service.LoadEasyListLite(listPath);
    return service;
}

static void AdBlockBlocksEasyListRules()
{
    var service = CreateAdBlockService();
    Assert(service.ShouldBlock("https://ads.example.com/banner.js", enabled: true), "host rule should block exact host");
    Assert(service.ShouldBlock("https://cdn.ads.example.com/banner.js", enabled: true), "host rule should block subdomain");
    Assert(service.ShouldBlock("https://site.example.com/assets/adserver/main.js", enabled: true), "URL contains rule should block");
    Assert(service.ShouldBlock("https://custom-ads.example.org/pixel.gif", enabled: true), "custom host should block");
    Assert(!service.ShouldBlock("https://allowed.example.com/file.js", enabled: true), "exception rule is ignored, not converted into block");
}

static void AdBlockHonorsWhitelist()
{
    var service = CreateAdBlockService();
    Assert(!service.ShouldBlock("https://ads.example.com/banner.js", enabled: false), "disabled blocker should not block");
    Assert(!service.ShouldBlock("https://ads.example.com/banner.js", enabled: true, ["example.com"]), "whitelist should bypass block");
}

static void AdBlockReplacesCustomHostsWithoutRebuildingService()
{
    var service = CreateAdBlockService();
    Assert(service.ShouldBlock("https://custom-ads.example.org/pixel.gif", enabled: true), "initial custom host should block");

    service.ReplaceCustomBlockedHosts(["fresh-ads.example.net"]);

    Assert(!service.ShouldBlock("https://custom-ads.example.org/pixel.gif", enabled: true), "removed custom host should stop blocking");
    Assert(service.ShouldBlock("https://fresh-ads.example.net/pixel.gif", enabled: true), "new custom host should block immediately");
}

static void AdBlockExplainsMatchedRule()
{
    var service = CreateAdBlockService();
    var host = service.Evaluate("https://cdn.ads.example.com/banner.js", enabled: true);
    var url = service.Evaluate("https://site.example/assets/adserver/main.js", enabled: true);
    var allowed = service.Evaluate("https://ads.example.com/banner.js", enabled: true, ["example.com"]);

    Assert(host.IsBlocked && host.Reason == "host" && host.Rule == "ads.example.com", "host decision should identify its rule");
    Assert(url.IsBlocked && url.Reason == "url" && url.Rule == "/adserver/", "URL decision should identify its rule");
    Assert(!allowed.IsBlocked && allowed.Reason == "whitelist", "allow decision should explain whitelist bypass");
}

static void CosmeticScriptIncludesSelectors()
{
    var service = CreateAdBlockService();
    var script = service.CreateCosmeticScript();
    Assert(script.Contains(".sponsored-card", StringComparison.Ordinal), "cosmetic selector should be injected");
    Assert(script.Contains("style.textContent", StringComparison.Ordinal), "cosmetic selectors should be applied through CSS");
    Assert(!script.Contains("MutationObserver", StringComparison.Ordinal), "cosmetic script should avoid DOM observers");
}

static void CosmeticScriptBypassesVerificationHosts()
{
    var service = CreateAdBlockService();
    var script = service.CreateCosmeticScript(BrowserCompatibilityPolicy.AdBlockBypassHosts);
    Assert(script.Contains("google.com", StringComparison.Ordinal), "cosmetic script should include compatibility hosts");
    Assert(script.Contains("host.endsWith", StringComparison.Ordinal), "cosmetic script should bypass compatibility subdomains");
}

static void EdgeAutoHideGeometryKeepsOneVisibleStrip()
{
    var restore = new EdgeAutoHideService.NativeRect { Left = 100, Top = 80, Right = 500, Bottom = 880 };
    var strip = (int)EdgeAutoHideService.VisibleStrip;

    var left = EdgeAutoHideService.GetHiddenBounds(restore, EdgeAutoHideService.EdgeSide.Left);
    Assert(left.Right == restore.Left + strip, "left hidden window should leave only the right strip visible");
    Assert(left.Width == restore.Width, "left hidden window should preserve width");

    var right = EdgeAutoHideService.GetHiddenBounds(restore, EdgeAutoHideService.EdgeSide.Right);
    Assert(right.Left == restore.Right - strip, "right hidden window should leave only the left strip visible");
    Assert(right.Width == restore.Width, "right hidden window should preserve width");

    var top = EdgeAutoHideService.GetHiddenBounds(restore, EdgeAutoHideService.EdgeSide.Top);
    Assert(top.Bottom == restore.Top + strip, "top hidden window should leave only the bottom strip visible");
    Assert(top.Height == restore.Height, "top hidden window should preserve height");

    var bottom = EdgeAutoHideService.GetHiddenBounds(restore, EdgeAutoHideService.EdgeSide.Bottom);
    Assert(bottom.Top == restore.Bottom - strip, "bottom hidden window should leave only the top strip visible");
    Assert(bottom.Height == restore.Height, "bottom hidden window should preserve height");
}

static void EdgeAutoHideRevealOnlyReactsOnVisibleStrip()
{
    var rect = new EdgeAutoHideService.NativeRect { Left = 496, Top = 80, Right = 896, Bottom = 880 };
    Assert(
        EdgeAutoHideService.IsPointOnVisibleStrip(
            rect,
            new EdgeAutoHideService.NativePoint { X = 498, Y = 300 },
            EdgeAutoHideService.EdgeSide.Right),
        "right hidden window should reveal from the visible left strip");
    Assert(
        !EdgeAutoHideService.IsPointOnVisibleStrip(
            rect,
            new EdgeAutoHideService.NativePoint { X = 880, Y = 300 },
            EdgeAutoHideService.EdgeSide.Right),
        "right hidden window should not reveal from off-screen body coordinates");
}

static void EdgeAutoHideWaitsForPointerEntryAfterReveal()
{
    Assert(
        !EdgeAutoHideService.ShouldArmAutoHide(isArmed: false, cursorOverWindow: false),
        "pointer on the external reveal strip must not immediately re-arm auto hide");
    Assert(
        EdgeAutoHideService.ShouldArmAutoHide(isArmed: false, cursorOverWindow: true),
        "entering the restored window should arm auto hide");
    Assert(
        EdgeAutoHideService.ShouldArmAutoHide(isArmed: true, cursorOverWindow: false),
        "leaving an armed window should keep it armed for the hide transition");
}

static void SettingsNormalizesSiteProfiles()
{
    var settings = new AppSettings
    {
        SiteProfiles =
        [
            new SiteProfile { Host = "https://Example.com/path", Opacity = double.NaN, SizePresetIndex = -10 },
            new SiteProfile { Host = "example.com", MobileMode = false, AdBlockEnabled = false, Opacity = 0.5, SizePresetIndex = 3 },
            new SiteProfile { Host = "   " }
        ],
        Windows =
        [
            new WindowProfile { Id = string.Empty, Width = double.PositiveInfinity, Height = 10, Opacity = double.NaN }
        ]
    };

    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();

    Assert(loaded.SiteProfiles.Count(profile => profile.Host == "example.com") == 1, "duplicate site hosts should be folded");
    var site = loaded.SiteProfiles.Single(profile => profile.Host == "example.com");
    Assert(site.MobileMode == false, "latest duplicate site profile should win");
    Assert(site.AdBlockEnabled == false, "site adblock setting should persist");
    Assert(Math.Abs(site.Opacity - 0.7) < 0.001, "site opacity should be clamped");
    Assert(site.SizePresetIndex == 3, "site size preset should persist when valid");
    Assert(loaded.Windows[0].Width == 390, "invalid window width should fall back");
    Assert(loaded.Windows[0].Height == 320, "window height should be clamped");
    Assert(loaded.Windows[0].Opacity == 1.0, "invalid window opacity should fall back");
    Assert(!string.IsNullOrWhiteSpace(loaded.Windows[0].Id), "window id should be regenerated");
}

static void SettingsDefaultsToGoogleSearch()
{
    var settings = new AppSettings { SearchEngineUrl = "https://example.com/search" };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.SearchEngineUrl == "https://www.google.com/search?q={query}", "invalid search template should fall back to Google");
}

static void SettingsDefaultsToDesktopCompatibilityMode()
{
    var settings = new AppSettings();
    Assert(!settings.MobileMode, "new windows should use the native desktop User-Agent by default");
    Assert(!settings.LowMemoryMode, "low-memory browser flags should be disabled by default");
    Assert(!new WindowProfile().MobileMode, "new window profiles should use desktop mode by default");
}

static void SettingsMigratesLegacyLowMemoryMode()
{
    var settings = new AppSettings { SettingsVersion = 0, LowMemoryMode = true };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.SettingsVersion == 5, "legacy settings should be upgraded");
    Assert(!loaded.LowMemoryMode, "legacy low-memory flags should be disabled during migration");
    Assert(loaded.CompatibilityCacheResetPending, "legacy browser cache should be reset once after upgrading");
}

static void SettingsMergesLegacyWindowsIntoTabs()
{
    var settings = new AppSettings
    {
        SettingsVersion = 3,
        Windows =
        [
            new WindowProfile { Url = "https://example.com/one" },
            new WindowProfile { Url = "https://example.com/two" }
        ]
    };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();

    Assert(loaded.Windows.Count == 1, "legacy windows should become one main window");
    Assert(loaded.Windows[0].Tabs.Count == 2, "each legacy window should become a tab");
    Assert(loaded.Windows[0].Tabs[0].Url == "https://example.com/one", "first legacy URL should be preserved");
    Assert(loaded.Windows[0].Tabs[1].Url == "https://example.com/two", "second legacy URL should be preserved");
    Assert(loaded.Windows[0].ActiveTabId == loaded.Windows[0].Tabs[0].Id, "the first migrated tab should be active");
}

static void SettingsMigrationDisablesLegacyEdgeAutoHide()
{
    var settings = new AppSettings
    {
        SettingsVersion = 4,
        EdgeAutoHideEnabled = true
    };
    var service = CreateSettingsService();

    service.Save(settings);
    var loaded = service.Load();

    Assert(loaded.SettingsVersion == 5, "edge migration should advance settings version");
    Assert(!loaded.EdgeAutoHideEnabled, "legacy auto-hide should be disabled until explicitly re-enabled");
}

static void SettingsReplacesTransientBlankTabUrls()
{
    var settings = new AppSettings
    {
        Windows =
        [
            new WindowProfile
            {
                Url = "about:blank",
                Tabs = [new TabProfile { Url = "about:blank" }]
            }
        ]
    };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();

    Assert(loaded.Windows[0].Tabs[0].Url == loaded.HomeUrl, "transient WebView2 blank URLs should restore to home");
}

static void CompatibilityPolicyBypassesVerificationHosts()
{
    Assert(BrowserCompatibilityPolicy.BypassAdBlockForHost("www.google.com"), "Google should bypass ad blocking");
    Assert(BrowserCompatibilityPolicy.BypassAdBlockForHost("challenges.cloudflare.com"), "Cloudflare challenges should bypass ad blocking");
    Assert(BrowserCompatibilityPolicy.BypassAdBlockForHost("login.microsoftonline.com"), "Microsoft OAuth should bypass ad blocking");
    Assert(BrowserCompatibilityPolicy.BypassAdBlockForHost("tenant.auth0.com"), "Auth0 login should bypass ad blocking");
    Assert(BrowserCompatibilityPolicy.BypassAdBlockForHost("checkout.stripe.com"), "Stripe checkout should bypass ad blocking");
    Assert(
        BrowserCompatibilityPolicy.BypassAdBlockForRequest("www.google.com", "doubleclick.net"),
        "all third-party requests on a verification-sensitive page should bypass ad blocking");
    Assert(!BrowserCompatibilityPolicy.BypassAdBlockForHost("ads.example.com"), "unrelated hosts should keep ad blocking");
}

static void SettingsNormalizesGoogleNcrStartupUrls()
{
    var settings = new AppSettings
    {
        HomeUrl = "https://www.google.com",
        LastUrl = "https://www.google.cn/m",
        Windows =
        [
            new WindowProfile { Url = "https://www.google.cn/m" }
        ]
    };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.HomeUrl == "https://www.google.com/ncr", "Google home should use NCR URL");
    Assert(loaded.LastUrl == "https://www.google.com/ncr", "broken Google China mobile URL should migrate to NCR");
    Assert(loaded.Windows[0].Url == "https://www.google.com/ncr", "window URL should migrate away from broken Google China mobile URL");
}

static void SettingsLoadMigratesBrokenGoogleStartupUrl()
{
    var dataDirectory = CreateTestDataDirectory();
    var settingsPath = Path.Combine(dataDirectory, "settings.json");
    File.WriteAllText(
        settingsPath,
        """
        {
          "HomeUrl": "https://www.google.com",
          "LastUrl": "https://www.google.cn/m",
          "SearchEngineUrl": "https://www.google.com/search?q={query}",
          "PopupPosition": "BottomRight",
          "Windows": [
            {
              "Id": "test",
              "Url": "https://www.google.cn/m",
              "Width": 390,
              "Height": 844,
              "Opacity": 1.0
            }
          ]
        }
        """);

    var loaded = new SettingsService(dataDirectory).Load();
    Assert(loaded.HomeUrl == "https://www.google.com/ncr", "loaded Google home should migrate to NCR");
    Assert(loaded.LastUrl == "https://www.google.com/ncr", "loaded broken Google China mobile URL should migrate to NCR");
    Assert(loaded.Windows[0].Url == "https://www.google.com/ncr", "loaded window URL should migrate to NCR");
}

static void TabsCreateAndActivateNewHomeTab()
{
    var profile = ProfileWithTabs("https://first.example");
    var service = new TabSessionService(profile, "https://home.example");

    var created = service.Create();

    Assert(service.Tabs.Count == 2, "new tab should be added to the session");
    Assert(service.ActiveTab.Id == created.Id, "new tab should become active");
    Assert(created.Url == "https://home.example", "new tab should use the configured home URL");
    Assert(profile.Url == created.Url, "window URL should follow the active tab");
}

static void SettingsCapsOversizedRestoredSessions()
{
    var tabs = Enumerable.Range(1, TabSessionService.MaximumTabs + 5)
        .Select(index => new TabProfile { Url = $"https://tab-{index}.example" })
        .ToList();
    var settings = new AppSettings
    {
        Windows =
        [
            new WindowProfile
            {
                Tabs = tabs,
                ActiveTabId = tabs[^1].Id
            }
        ]
    };
    var service = CreateSettingsService();

    service.Save(settings);
    var loaded = service.Load();

    Assert(loaded.Windows[0].Tabs.Count == TabSessionService.MaximumTabs, "restored session should be capped at 20 tabs");
    Assert(loaded.Windows[0].ActiveTabId == loaded.Windows[0].Tabs[0].Id, "removed active tab should fall back to the first tab");
}

static void TabsCloseActiveTabAndSelectNeighbor()
{
    var profile = ProfileWithTabs("https://one.example", "https://two.example", "https://three.example");
    var service = new TabSessionService(profile, "https://home.example");
    service.Activate(profile.Tabs[1].Id);

    var result = service.Close(profile.Tabs[1].Id);

    Assert(result.Removed?.Url == "https://two.example", "requested tab should be removed");
    Assert(service.ActiveTab.Url == "https://three.example", "right neighbor should become active");
    Assert(service.Tabs.Count == 2, "one tab should be removed");
}

static void TabsCloseBackgroundTabWithoutChangingActive()
{
    var profile = ProfileWithTabs("https://one.example", "https://two.example");
    var service = new TabSessionService(profile, "https://home.example");
    var activeId = service.ActiveTab.Id;

    var result = service.Close(profile.Tabs[1].Id);

    Assert(!result.ActiveChanged, "closing a background tab should not change active tab");
    Assert(service.ActiveTab.Id == activeId, "active tab should remain selected");
}

static void TabsReplaceFinalTabWithHome()
{
    var profile = ProfileWithTabs("https://work.example");
    var service = new TabSessionService(profile, "https://home.example");
    var oldId = service.ActiveTab.Id;

    service.Close(oldId);

    Assert(service.Tabs.Count == 1, "closing the final tab should retain one tab");
    Assert(service.ActiveTab.Id != oldId, "final tab should be replaced, not reused");
    Assert(service.ActiveTab.Url == "https://home.example", "replacement should use the home URL");
}

static void TabsCycleInBothDirections()
{
    var profile = ProfileWithTabs("https://one.example", "https://two.example", "https://three.example");
    var service = new TabSessionService(profile, "https://home.example");

    Assert(service.ActivateRelative(-1).Url == "https://three.example", "previous should wrap to final tab");
    Assert(service.ActivateRelative(1).Url == "https://one.example", "next should wrap to first tab");
}

static void TabsRepairInvalidActiveId()
{
    var profile = ProfileWithTabs("https://one.example", "https://two.example");
    profile.ActiveTabId = "missing";

    var service = new TabSessionService(profile, "https://home.example");

    Assert(service.ActiveTab.Id == profile.Tabs[0].Id, "invalid active id should select the first tab");
}

static void TabsEnforceSessionLimit()
{
    var urls = Enumerable.Range(1, TabSessionService.MaximumTabs)
        .Select(index => $"https://tab-{index}.example")
        .ToArray();
    var profile = ProfileWithTabs(urls);
    var service = new TabSessionService(profile, "https://home.example");

    var rejected = false;
    try
    {
        service.Create();
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert(rejected, "creating tab 21 should be rejected");
    Assert(service.Tabs.Count == TabSessionService.MaximumTabs, "rejected tab should not mutate the session");
}

static void TabSuspensionProtectsSensitiveActivity()
{
    Assert(TabSuspensionPolicy.CanSuspend("news.example", false, 0), "ordinary inactive tab should be suspendable");
    Assert(!TabSuspensionPolicy.CanSuspend("news.example", true, 0), "audio playback should prevent suspension");
    Assert(!TabSuspensionPolicy.CanSuspend("news.example", false, 1), "active download should prevent suspension");
    Assert(!TabSuspensionPolicy.CanSuspend("login.microsoftonline.com", false, 0), "authentication flow should prevent suspension");
}

static WindowProfile ProfileWithTabs(params string[] urls)
{
    var profile = new WindowProfile { Tabs = urls.Select(url => new TabProfile { Url = url }).ToList() };
    profile.ActiveTabId = profile.Tabs[0].Id;
    return profile;
}

static void NavigationResolvesPublicHostsWithHttps()
{
    var resolved = NavigationService.Resolve("example.com/path", "https://home.example", null);
    Assert(resolved == "https://example.com/path", "public host should default to HTTPS");
}

static void NavigationResolvesLocalHostsWithHttp()
{
    var localhost = NavigationService.Resolve("localhost:8080/page", "https://home.example", null);
    var loopback = NavigationService.Resolve("127.0.0.1:5000", "https://home.example", null);
    Assert(localhost == "http://localhost:8080/page", "localhost should default to HTTP");
    Assert(loopback == "http://127.0.0.1:5000/", "loopback IP should default to HTTP");
}

static void NavigationBuildsEscapedSearchUrl()
{
    var resolved = NavigationService.Resolve(
        "mini browser 测试",
        "https://home.example",
        "https://search.example/?q={query}");
    Assert(resolved == "https://search.example/?q=mini%20browser%20%E6%B5%8B%E8%AF%95", "query should be URL escaped once");
}

static void NavigationRejectsInvalidSearchTemplates()
{
    var resolved = NavigationService.Resolve("two words", "https://home.example", "https://invalid.example/search");
    Assert(resolved == "https://www.google.com/search?q=two%20words", "invalid template should fall back to Google");
}

static void WindowPlacementAnchorsAllPositions()
{
    var left = WindowPlacementService.Calculate(0, 0, 1920, 1040, 390, 844, "BottomLeft");
    var center = WindowPlacementService.Calculate(0, 0, 1920, 1040, 390, 844, "BottomCenter");
    var right = WindowPlacementService.Calculate(0, 0, 1920, 1040, 390, 844, "BottomRight");

    Assert(left.Left == 8, "left placement should preserve the work-area margin");
    Assert(center.Left == 765, "center placement should use the work-area center");
    Assert(right.Left == 1522, "right placement should preserve the work-area margin");
    Assert(left.Top == 188 && center.Top == 188 && right.Top == 188, "all placements should align to the bottom margin");
}

static void WindowPlacementClampsOversizedWindows()
{
    var bounds = WindowPlacementService.Calculate(100, 50, 500, 350, 900, 800, "BottomRight");
    Assert(bounds.Left == 108 && bounds.Top == 58, "oversized popup should remain inside the work area");
    Assert(bounds.Width == 384 && bounds.Height == 284, "oversized popup should expose its safe rendered size");
}

static void WindowPlacementRemainsVisibleAtCommonDpiScales()
{
    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
    {
        var right = 1920 / scale;
        var bottom = 1040 / scale;
        var bounds = WindowPlacementService.Calculate(0, 0, right, bottom, 390, 844, "BottomRight");
        Assert(bounds.Left >= 8 && bounds.Top >= 8, $"{scale:P0} popup should stay above the work-area origin");
        Assert(bounds.Left + bounds.Width <= right - 8.0 + 0.001, $"{scale:P0} popup should stay inside the right edge");
        Assert(bounds.Top + bounds.Height <= bottom - 8.0 + 0.001, $"{scale:P0} popup should stay inside the bottom edge");
    }
}

static void SingleInstanceSignalsPrimary()
{
    var id = "MiniBrowser.SelfTest." + Guid.NewGuid().ToString("N");
    using var primary = new SingleInstanceService(id);
    using var secondary = new SingleInstanceService(id);
    using var activated = new ManualResetEventSlim();

    Assert(primary.IsPrimary, "first coordinator should own the application instance");
    Assert(!secondary.IsPrimary, "second coordinator should detect the existing instance");
    primary.StartListening(activated.Set);
    secondary.SignalPrimary();

    Assert(activated.Wait(TimeSpan.FromSeconds(2)), "secondary instance should wake the primary listener");
}

static void SettingsSkipsIdenticalRepeatedSaves()
{
    var dataDirectory = CreateTestDataDirectory();
    var backupPath = Path.Combine(dataDirectory, "settings.backup.json");
    var service = new SettingsService(dataDirectory);
    var settings = new AppSettings { HomeUrl = "https://www.google.com/ncr" };
    service.Save(settings);
    File.Delete(backupPath);
    service.Save(settings);

    Assert(!File.Exists(backupPath), "identical repeated save should not create a backup file");
}

static void SettingsDefaultsPopupPositionToBottomRight()
{
    var settings = new AppSettings { PopupPosition = "Floating" };
    var service = CreateSettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.PopupPosition == "BottomRight", "invalid popup position should fall back to bottom right");
}

static void SettingsDisablesEdgeAutoHideByDefault()
{
    var loaded = CreateSettingsService().Load();
    Assert(!loaded.EdgeAutoHideEnabled, "edge auto hide should remain an opt-in feature");
}

static void SettingsDisablesStartupUpdateChecksByDefault()
{
    var loaded = CreateSettingsService().Load();
    Assert(!loaded.AutoCheckUpdates, "startup update checks should default to disabled");
}

static void UpdateParserFindsNewerPortableRelease()
{
    using var document = JsonDocument.Parse(
        """
        {
          "tag_name": "v99.0.0",
          "html_url": "https://github.com/zhuchengxue/MiniBrowser/releases/tag/v99.0.0",
          "assets": [
            {
              "name": "MiniBrowser-Portable.zip",
              "browser_download_url": "https://example.com/MiniBrowser-Portable.zip"
            },
            {
              "name": "MiniBrowser-Setup.zip",
              "browser_download_url": "https://example.com/MiniBrowser-Setup.zip"
            }
          ]
        }
        """);

    var result = UpdateService.ParseRelease(document.RootElement);
    Assert(result.IsAvailable, "newer release should be available");
    Assert(result.Asset is not null, "portable asset should be selected");
    Assert(result.Asset!.Name == AppInfo.PortableAssetName, "portable asset name should match");
    Assert(result.Asset.DownloadUrl.EndsWith(AppInfo.PortableAssetName, StringComparison.Ordinal), "download URL should be kept");
}

static void UpdateParserTreatsCurrentReleaseAsCurrent()
{
    using var document = JsonDocument.Parse(
        $$"""
        {
          "tag_name": "v{{AppInfo.Version}}",
          "html_url": "https://github.com/zhuchengxue/MiniBrowser/releases/tag/v{{AppInfo.Version}}",
          "assets": [
            {
              "name": "MiniBrowser-Portable.zip",
              "browser_download_url": "https://example.com/MiniBrowser-Portable.zip"
            }
          ]
        }
        """);

    var result = UpdateService.ParseRelease(document.RootElement);
    Assert(!result.IsAvailable, "current release should not be available as update");
    Assert(!result.IsUnavailable, "current release should not be treated as unavailable");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static SettingsService CreateSettingsService()
{
    return new SettingsService(CreateTestDataDirectory());
}

static string CreateTestDataDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "MiniBrowser.SelfTest", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}
