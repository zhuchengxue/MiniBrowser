using System.IO;
using System.Text.Json;
using MiniBrowser.App.Infrastructure;
using MiniBrowser.App.Models;

namespace MiniBrowser.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string _backupPath;

    public SettingsService()
    {
        _settingsPath = RuntimePaths.SettingsPath;
        _backupPath = RuntimePaths.SettingsBackupPath;
        TryMigrateLegacySettings();
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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load settings.");
            var backup = TryLoadBackup();
            if (backup is not null)
            {
                return backup;
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_settingsPath))
        {
            File.Copy(_settingsPath, _backupPath, overwrite: true);
        }

        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.HomeUrl = NormalizeUrl(settings.HomeUrl, "https://www.bing.com");
        settings.LastUrl = NormalizeUrl(settings.LastUrl, settings.HomeUrl);
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

        foreach (var window in settings.Windows)
        {
            window.Url = NormalizeUrl(window.Url, settings.HomeUrl);
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
        }
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
