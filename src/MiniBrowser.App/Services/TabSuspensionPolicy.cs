namespace MiniBrowser.App.Services;

public static class TabSuspensionPolicy
{
    public static bool CanSuspend(string topLevelHost, bool isPlayingAudio, int activeDownloads)
    {
        return !isPlayingAudio &&
               activeDownloads == 0 &&
               !BrowserCompatibilityPolicy.BypassAdBlockForHost(topLevelHost);
    }
}
