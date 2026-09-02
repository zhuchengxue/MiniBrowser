namespace MiniBrowser.App.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 5;
    public bool CompatibilityCacheResetPending { get; set; }
    public string HomeUrl { get; set; } = "https://www.google.com/ncr";
    public string LastUrl { get; set; } = "https://www.google.com/ncr";
    public string SearchEngineUrl { get; set; } = "https://www.google.com/search?q={query}";
    public string PopupPosition { get; set; } = "BottomRight";
    public bool EdgeAutoHideEnabled { get; set; }
    public bool GlobalHotkeyEnabled { get; set; } = true;
    public bool LowMemoryMode { get; set; }
    public bool AutoCheckUpdates { get; set; }
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public bool AdBlockEnabled { get; set; } = true;
    public List<string> AdBlockWhitelist { get; set; } = [];
    public List<string> CustomBlockedHosts { get; set; } = [];
    public List<WindowProfile> Windows { get; set; } = [];
    public List<SiteProfile> SiteProfiles { get; set; } = [];
    public double WindowWidth { get; set; } = 390;
    public double WindowHeight { get; set; } = 844;
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowOpacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool MobileMode { get; set; }
    public bool ChromeVisible { get; set; } = true;
    public int SizePresetIndex { get; set; }
    public List<QuickSite> QuickSites { get; set; } =
    [
        new("ChatGPT", "https://chat.openai.com"),
        new("Google", "https://www.google.com/ncr"),
        new("YouTube", "https://m.youtube.com"),
        new("WeRead", "https://weread.qq.com")
    ];
}

public sealed record QuickSite(string Name, string Url);

public sealed class SiteProfile
{
    public string Host { get; set; } = string.Empty;
    public bool MobileMode { get; set; }
    public bool AdBlockEnabled { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool ChromeVisible { get; set; } = true;
    public bool Borderless { get; set; }
    public double Opacity { get; set; } = 1.0;
    public int SizePresetIndex { get; set; }
}

public sealed class WindowProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "MiniBrowser";
    public string Url { get; set; } = "https://www.google.com/ncr";
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 844;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double Opacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool MobileMode { get; set; }
    public bool ChromeVisible { get; set; } = true;
    public bool Borderless { get; set; }
    public bool AdBlockEnabled { get; set; } = true;
    public int SizePresetIndex { get; set; }
    public string ActiveTabId { get; set; } = string.Empty;
    public List<TabProfile> Tabs { get; set; } = [];
}

public sealed class TabProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New tab";
    public string Url { get; set; } = "https://www.google.com/ncr";
}
