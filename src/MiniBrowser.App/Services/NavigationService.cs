using System.Net;

namespace MiniBrowser.App.Services;

public static class NavigationService
{
    private const string GoogleSearchUrl = "https://www.google.com/search?q={query}";

    public static string Resolve(string? raw, string homeUrl, string? searchEngineUrl)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return homeUrl;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (!value.Contains(' ') && LooksLikeHost(value))
        {
            var scheme = IsLocalHost(value) ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
            return new Uri($"{scheme}://{value}").ToString();
        }

        var template = string.IsNullOrWhiteSpace(searchEngineUrl) ||
                       !searchEngineUrl.Contains("{query}", StringComparison.OrdinalIgnoreCase)
            ? GoogleSearchUrl
            : searchEngineUrl;
        return template.Replace("{query}", Uri.EscapeDataString(value), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHost(string value)
    {
        var authority = value.Split('/', 2)[0];
        var host = authority.Split(':', 2)[0].Trim('[', ']');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out _) ||
               host.Contains('.');
    }

    private static bool IsLocalHost(string value)
    {
        var authority = value.Split('/', 2)[0];
        var host = authority.Split(':', 2)[0].Trim('[', ']');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
