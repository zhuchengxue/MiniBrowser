using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;
using System.Text.Json;

var tests = new (string Name, Action Body)[]
{
    ("AdBlock blocks EasyList hosts and URL rules", AdBlockBlocksEasyListRules),
    ("AdBlock honors whitelist", AdBlockHonorsWhitelist),
    ("Cosmetic script includes EasyList selectors", CosmeticScriptIncludesSelectors),
    ("Settings normalizes site profiles", SettingsNormalizesSiteProfiles),
    ("Settings defaults to Google search", SettingsDefaultsToGoogleSearch),
    ("Settings defaults popup position to bottom right", SettingsDefaultsPopupPositionToBottomRight),
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

static void CosmeticScriptIncludesSelectors()
{
    var service = CreateAdBlockService();
    var script = service.CreateCosmeticScript();
    Assert(script.Contains(".sponsored-card", StringComparison.Ordinal), "cosmetic selector should be injected");
    Assert(script.Contains("MutationObserver", StringComparison.Ordinal), "cosmetic script should observe DOM changes");
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

    var service = new SettingsService();
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
    var service = new SettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.SearchEngineUrl == "https://www.google.com/search?q={query}", "invalid search template should fall back to Google");
}

static void SettingsDefaultsPopupPositionToBottomRight()
{
    var settings = new AppSettings { PopupPosition = "Floating" };
    var service = new SettingsService();
    service.Save(settings);
    var loaded = service.Load();
    Assert(loaded.PopupPosition == "BottomRight", "invalid popup position should fall back to bottom right");
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
