namespace MiniBrowser.App.Services;

public static class BrowserCompatibilityPolicy
{
    public static IReadOnlyList<string> AdBlockBypassHosts { get; } =
    [
        "google.com",
        "googleapis.com",
        "gstatic.com",
        "bing.com",
        "bingapis.com",
        "microsoft.com",
        "live.com",
        "cloudflare.com",
        "challenges.cloudflare.com"
    ];

    public static bool BypassAdBlockForHost(string host)
    {
        return !string.IsNullOrWhiteSpace(host) &&
               AdBlockBypassHosts.Any(candidate => HostMatches(host, candidate));
    }

    public static bool BypassAdBlockForRequest(string topLevelHost, string requestHost)
    {
        return BypassAdBlockForHost(topLevelHost) || BypassAdBlockForHost(requestHost);
    }

    private static bool HostMatches(string host, string candidate)
    {
        return host.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase);
    }
}
