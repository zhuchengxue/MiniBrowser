namespace MiniBrowser.App.Models;

public sealed class AppSettings
{
    public string HomeUrl { get; set; } = "https://www.bing.com";
    public string LastUrl { get; set; } = "https://www.bing.com";
    public bool GlobalHotkeyEnabled { get; set; } = true;
    public bool LowMemoryMode { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public bool AdBlockEnabled { get; set; } = true;
    public List<string> AdBlockWhitelist { get; set; } = [];
    public List<string> CustomBlockedHosts { get; set; } = [];
    public List<WindowProfile> Windows { get; set; } = [];
    public double WindowWidth { get; set; } = 390;
    public double WindowHeight { get; set; } = 844;
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowOpacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool MobileMode { get; set; } = true;
    public bool ChromeVisible { get; set; } = true;
    public int SizePresetIndex { get; set; }
    public List<QuickSite> QuickSites { get; set; } =
    [
        new("ChatGPT", "https://chat.openai.com"),
        new("Bing", "https://www.bing.com"),
        new("YouTube", "https://m.youtube.com"),
        new("WeRead", "https://weread.qq.com")
    ];
}

public sealed record QuickSite(string Name, string Url);

public sealed class WindowProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "MiniBrowser";
    public string Url { get; set; } = "https://www.bing.com";
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 844;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double Opacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool MobileMode { get; set; } = true;
    public bool ChromeVisible { get; set; } = true;
    public bool Borderless { get; set; }
    public bool AdBlockEnabled { get; set; } = true;
    public int SizePresetIndex { get; set; }
}
