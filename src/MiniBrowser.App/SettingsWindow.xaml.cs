using System.Windows;
using MiniBrowser.App.Models;
using MiniBrowser.App.Services;

namespace MiniBrowser.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    public SettingsWindow(SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = settings;

        HomeUrlBox.Text = _settings.HomeUrl;
        HotkeyCheck.IsChecked = _settings.GlobalHotkeyEnabled;
        AdBlockCheck.IsChecked = _settings.AdBlockEnabled;
        AutoUpdateCheck.IsChecked = _settings.AutoCheckUpdates;
        QuickSitesBox.Text = string.Join(Environment.NewLine, _settings.QuickSites.Select(site => $"{site.Name}|{site.Url}"));
        WhitelistBox.Text = string.Join(Environment.NewLine, _settings.AdBlockWhitelist);
        SiteProfilesBox.Text = string.Join(Environment.NewLine, _settings.SiteProfiles.Select(FormatSiteProfile));
        BlockedHostsBox.Text = string.Join(Environment.NewLine, _settings.CustomBlockedHosts);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.HomeUrl = HomeUrlBox.Text.Trim();
        _settings.GlobalHotkeyEnabled = HotkeyCheck.IsChecked == true;
        _settings.AdBlockEnabled = AdBlockCheck.IsChecked == true;
        _settings.AutoCheckUpdates = AutoUpdateCheck.IsChecked == true;
        _settings.QuickSites = ParseSites(QuickSitesBox.Text).ToList();
        _settings.AdBlockWhitelist = ParseLines(WhitelistBox.Text).ToList();
        _settings.SiteProfiles = ParseSiteProfiles(SiteProfilesBox.Text).ToList();
        _settings.CustomBlockedHosts = ParseLines(BlockedHostsBox.Text).ToList();
        _settingsService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static IEnumerable<QuickSite> ParseSites(string text)
    {
        foreach (var line in ParseLines(text))
        {
            var parts = line.Split('|', 2);
            if (parts.Length == 2)
            {
                yield return new QuickSite(parts[0].Trim(), parts[1].Trim());
            }
        }
    }

    private static string FormatSiteProfile(SiteProfile profile)
    {
        return string.Join(
            "|",
            profile.Host,
            profile.MobileMode,
            profile.AdBlockEnabled,
            profile.SizePresetIndex,
            profile.Topmost,
            profile.Borderless,
            profile.ChromeVisible,
            Math.Round(profile.Opacity, 2));
    }

    private static IEnumerable<SiteProfile> ParseSiteProfiles(string text)
    {
        foreach (var line in ParseLines(text))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            yield return new SiteProfile
            {
                Host = parts[0],
                MobileMode = ParseBool(parts, 1, true),
                AdBlockEnabled = ParseBool(parts, 2, true),
                SizePresetIndex = ParseInt(parts, 3, 0),
                Topmost = ParseBool(parts, 4, true),
                Borderless = ParseBool(parts, 5, false),
                ChromeVisible = ParseBool(parts, 6, true),
                Opacity = ParseDouble(parts, 7, 1.0)
            };
        }
    }

    private static bool ParseBool(string[] parts, int index, bool fallback)
    {
        return parts.Length > index && bool.TryParse(parts[index], out var value) ? value : fallback;
    }

    private static int ParseInt(string[] parts, int index, int fallback)
    {
        return parts.Length > index && int.TryParse(parts[index], out var value) ? value : fallback;
    }

    private static double ParseDouble(string[] parts, int index, double fallback)
    {
        return parts.Length > index && double.TryParse(parts[index], out var value) ? value : fallback;
    }

    private static IEnumerable<string> ParseLines(string text)
    {
        return text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }
}
