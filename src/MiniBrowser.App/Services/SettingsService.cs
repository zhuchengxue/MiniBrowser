using System.IO;
using System.Text.Json;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;

namespace MiniBrowser.App.Services;

public sealed class SettingsService
{
    private const string GoogleNcrUrl = "https://www.google.com/ncr";
    private const string GoogleSearchUrl = "https://www.google.com/search?q={query}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _backupPath;
    private string? _lastSavedJson;

    public SettingsService(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? RuntimePaths.DataDirectory;
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        _backupPath = Path.Combine(directory, "settings.backup.json");
        if (dataDirectory is null)
        {
            TryMigrateLegacySettings();
        }
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            _lastSavedJson = json;
            Normalize(settings);
            return settings;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load settings.");
            var backup = TryLoadBackup();
            if (backup is not null)
            {
                Normalize(backup);
                return backup;
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        if (_lastSavedJson == json)
        {
            return;
        }

        if (_lastSavedJson is null && File.Exists(_settingsPath) && File.ReadAllText(_settingsPath) == json)
        {
            _lastSavedJson = json;
            return;
        }

        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_settingsPath))
        {
            File.Copy(_settingsPath, _backupPath, overwrite: true);
        }

        File.Move(tempPath, _settingsPath, overwrite: true);
        _lastSavedJson = json;
    }

    private static void Normalize(AppSettings settings)
    {
        // Version 2 disables legacy low-memory flags once, because they can break bot checks and login flows.
        if (settings.SettingsVersion < 2)
        {
            settings.LowMemoryMode = false;
            settings.SettingsVersion = 2;
        }

        if (settings.SettingsVersion < 3)
        {
            settings.CompatibilityCacheResetPending = true;
            settings.SettingsVersion = 3;
        }

        if (settings.SettingsVersion < 4)
        {
            MergeLegacyWindowsIntoTabs(settings);
            settings.SettingsVersion = 4;
        }

        // Version 5 makes the historically fragile edge behavior opt-in.
        if (settings.SettingsVersion < 5)
        {
            settings.EdgeAutoHideEnabled = false;
            settings.SettingsVersion = 5;
        }

        settings.LowMemoryMode = false;

        settings.HomeUrl = NormalizeGoogleHomeUrl(settings.HomeUrl);
        settings.LastUrl = NormalizeUrl(settings.LastUrl, settings.HomeUrl);
        settings.LastUrl = NormalizeGoogleStartUrl(settings.LastUrl);
        settings.SearchEngineUrl = NormalizeSearchEngine(settings.SearchEngineUrl);
        settings.PopupPosition = NormalizePopupPosition(settings.PopupPosition);
        settings.WindowWidth = NormalizeRange(settings.WindowWidth, 390, 240, 3000);
        settings.WindowHeight = NormalizeRange(settings.WindowHeight, 844, 320, 3000);
        settings.WindowLeft = NormalizePosition(settings.WindowLeft);
        settings.WindowTop = NormalizePosition(settings.WindowTop);
        settings.WindowOpacity = NormalizeRange(settings.WindowOpacity, 1.0, 0.7, 1.0);
        settings.SizePresetIndex = Math.Max(0, settings.SizePresetIndex);
        settings.SiteProfiles = settings.SiteProfiles
            .Where(site => !string.IsNullOrWhiteSpace(site.Host))
            .GroupBy(site => NormalizeHost(site.Host), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var site = group.Last();
                site.Host = group.Key;
                site.Opacity = NormalizeRange(site.Opacity, 1.0, 0.7, 1.0);
                site.SizePresetIndex = Math.Max(0, site.SizePresetIndex);
                return site;
            })
            .OrderBy(site => site.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.Windows.Count > 1)
        {
            MergeLegacyWindowsIntoTabs(settings);
        }

        foreach (var window in settings.Windows)
        {
            window.Url = NormalizeUrl(window.Url, settings.HomeUrl);
            window.Url = NormalizeGoogleStartUrl(window.Url);
            window.Width = NormalizeRange(window.Width, 390, 240, 3000);
            window.Height = NormalizeRange(window.Height, 844, 320, 3000);
            window.Left = NormalizePosition(window.Left);
            window.Top = NormalizePosition(window.Top);
            window.Opacity = NormalizeRange(window.Opacity, 1.0, 0.7, 1.0);
            window.SizePresetIndex = Math.Max(0, window.SizePresetIndex);
            if (string.IsNullOrWhiteSpace(window.Id))
            {
                window.Id = Guid.NewGuid().ToString("N");
            }

            window.Tabs ??= [];
            foreach (var tab in window.Tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.Id))
                {
                    tab.Id = Guid.NewGuid().ToString("N");
                }

                tab.Url = NormalizeBrowserUrl(tab.Url, settings.HomeUrl);
                tab.Title = string.IsNullOrWhiteSpace(tab.Title) ? "New tab" : tab.Title.Trim();
            }

            window.Tabs = window.Tabs
                .GroupBy(tab => tab.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Take(TabSessionService.MaximumTabs)
                .ToList();
            if (window.Tabs.Count == 0)
            {
                window.Tabs.Add(new TabProfile { Url = window.Url });
            }

            if (!window.Tabs.Any(tab => tab.Id == window.ActiveTabId))
            {
                window.ActiveTabId = window.Tabs[0].Id;
            }

            var activeTab = window.Tabs.First(tab => tab.Id == window.ActiveTabId);
            window.Url = activeTab.Url;
        }
    }

    private static void MergeLegacyWindowsIntoTabs(AppSettings settings)
    {
        if (settings.Windows.Count == 0)
        {
            return;
        }

        var windows = settings.Windows.ToList();
        var primary = windows[0];
        var tabs = windows
            .SelectMany(window => window.Tabs is { Count: > 0 }
                ? window.Tabs
                : [new TabProfile { Title = window.Title, Url = window.Url }])
            .ToList();
        primary.Tabs = tabs;
        primary.ActiveTabId = tabs[0].Id;
        settings.Windows = [primary];
    }

    private static double NormalizeRange(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static double NormalizePosition(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return -1;
        }

        return value;
    }

    private static string NormalizeUrl(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeBrowserUrl(string value, string fallback)
    {
        var normalized = NormalizeGoogleStartUrl(NormalizeUrl(value, fallback));
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? normalized
            : fallback;
    }

    private static string NormalizeGoogleHomeUrl(string value)
    {
        return NormalizeGoogleStartUrl(NormalizeUrl(value, GoogleNcrUrl));
    }

    private static string NormalizeGoogleStartUrl(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        var isGoogleDotCom = host is "www.google.com" or "google.com";
        var isGoogleDotCn = host is "www.google.cn" or "google.cn";
        var isGoogleHome = isGoogleDotCom && (path is "" or "/");
        var isBrokenChinaMobileGoogle = isGoogleDotCn && path is "/m";
        return isGoogleHome || isBrokenChinaMobileGoogle ? GoogleNcrUrl : trimmed;
    }

    private static string NormalizeSearchEngine(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value)
            ? GoogleSearchUrl
            : value.Trim();
        return trimmed.Contains("{query}", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : GoogleSearchUrl;
    }

    private static string NormalizePopupPosition(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "BottomRight" : value.Trim();
        return trimmed switch
        {
            "BottomLeft" or "BottomCenter" or "BottomRight" => trimmed,
            _ => "BottomRight"
        };
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

    private void TryMigrateLegacySettings()
    {
        if (File.Exists(_settingsPath))
        {
            return;
        }

        var legacyAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyPath = Path.Combine(legacyAppData, "MiniBrowser", "settings.json");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, _settingsPath, overwrite: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to migrate legacy settings.");
            // Portable settings are optional; fall back to defaults if migration fails.
        }
    }

    private AppSettings? TryLoadBackup()
    {
        if (!File.Exists(_backupPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_backupPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load settings backup.");
            return null;
        }
    }
}
