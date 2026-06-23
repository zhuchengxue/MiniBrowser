using System.IO;
using System.Text.Json;
using MiniBrowser.App.Infrastructure;

namespace MiniBrowser.App.Services;

public sealed class AdBlockService
{
    private readonly HashSet<string> _blockedHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _urlContainsRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _cosmeticSelectors = [];

    private static readonly string[] DefaultHosts =
    [
        "doubleclick.net",
        "googleadservices.com",
        "googlesyndication.com",
        "adservice.google.com",
        "ads-twitter.com",
        "scorecardresearch.com",
        "taboola.com",
        "outbrain.com",
        "criteo.com",
        "adnxs.com",
        "adsafeprotected.com",
        "amazon-adsystem.com",
        "googletagservices.com",
        "imasdk.googleapis.com",
        "moatads.com",
        "quantserve.com",
        "zedo.com"
    ];

    private static readonly string[] DefaultUrlRules =
    [
        "/ads/",
        "/adserver/",
        "/advert/",
        "/banner/",
        "/banners/",
        "/sponsor/",
        "pagead2.",
        "adservice.",
        "googleads.",
        "prebid",
        "bidder",
        "analytics.js",
        "collect?",
        "pixel?",
        "tracking"
    ];

    private static readonly string[] DefaultCosmeticSelectors =
    [
        "iframe[src*='ads']",
        "iframe[id*='ad_']",
        "iframe[name*='ad']",
        "[id^='ad-']",
        "[id*='-ad-']",
        "[id*='_ad_']",
        "[id*='advert']",
        "[class~='ad']",
        "[class^='ad-']",
        "[class*=' ad-']",
        "[class*='-ad-']",
        "[class*='advert']",
        "[class*='sponsor']",
        "[class*='promoted']",
        "[aria-label*='advertisement' i]",
        "[data-ad]",
        "[data-ad-client]",
        "[data-ad-slot]",
        "[data-testid*='ad']"
    ];

    public int HostRuleCount => _blockedHosts.Count;
    public int UrlRuleCount => _urlContainsRules.Count;
    public int CosmeticRuleCount => _cosmeticSelectors.Count;

    public AdBlockService(IEnumerable<string>? customBlockedHosts = null)
    {
        foreach (var host in DefaultHosts)
        {
            AddHost(host);
        }

        foreach (var rule in DefaultUrlRules)
        {
            AddUrlContainsRule(rule);
        }

        foreach (var selector in DefaultCosmeticSelectors)
        {
            AddCosmeticSelector(selector);
        }

        foreach (var host in customBlockedHosts ?? [])
        {
            AddHost(host);
        }
    }

    public void AddHost(string host)
    {
        var normalized = NormalizeHost(host);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            _blockedHosts.Add(normalized);
        }
    }

    public void AddUrlContainsRule(string rule)
    {
        var normalized = NormalizeUrlRule(rule);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            _urlContainsRules.Add(normalized);
        }
    }

    public void AddCosmeticSelector(string selector)
    {
        var trimmed = selector.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.Contains('{') && !trimmed.Contains('}'))
        {
            _cosmeticSelectors.Add(trimmed);
        }
    }

    public void LoadEasyListLite(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            ParseRule(line);
        }
    }

    public bool ShouldBlock(string? rawUrl, bool enabled, IEnumerable<string>? whitelist = null)
    {
        if (!enabled || string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host;
        if (MatchesAnyHost(host, whitelist ?? []))
        {
            return false;
        }

        if (_blockedHosts.Any(blocked => HostMatches(host, blocked)))
        {
            return true;
        }

        var absolute = uri.AbsoluteUri;
        return _urlContainsRules.Any(rule => absolute.Contains(rule, StringComparison.OrdinalIgnoreCase));
    }

    public string CreateCosmeticScript()
    {
        var selectors = _cosmeticSelectors
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToArray();
        var selectorJson = JsonSerializer.Serialize(selectors);

        return $$"""
            (() => {
              const selectors = {{selectorJson}};
              const styleId = "mini-browser-ad-hide";
              const css = selectors.join(",\n") + "{display:none!important;visibility:hidden!important;}";
              let style = document.getElementById(styleId);
              if (!style) {
                style = document.createElement("style");
                style.id = styleId;
                document.documentElement.appendChild(style);
              }
              style.textContent = css;

              const hide = () => {
                for (const selector of selectors) {
                  try {
                    document.querySelectorAll(selector).forEach(node => {
                      node.style.setProperty("display", "none", "important");
                      node.style.setProperty("visibility", "hidden", "important");
                    });
                  } catch {}
                }
              };
              hide();
              if (!window.__miniBrowserAdObserver) {
                window.__miniBrowserAdObserver = new MutationObserver(() => {
                  clearTimeout(window.__miniBrowserAdTimer);
                  window.__miniBrowserAdTimer = setTimeout(hide, 80);
                });
                window.__miniBrowserAdObserver.observe(document.documentElement, { childList: true, subtree: true });
              }
            })();
            """;
    }

    private void ParseRule(string rawLine)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) ||
            line.StartsWith('!') ||
            line.StartsWith('[') ||
            line.StartsWith("@@", StringComparison.Ordinal))
        {
            return;
        }

        var cosmeticIndex = line.IndexOf("##", StringComparison.Ordinal);
        if (cosmeticIndex >= 0)
        {
            AddCosmeticSelector(line[(cosmeticIndex + 2)..]);
            return;
        }

        var optionIndex = line.IndexOf('$');
        if (optionIndex >= 0)
        {
            line = line[..optionIndex];
        }

        if (line.StartsWith("||", StringComparison.Ordinal))
        {
            var end = line.IndexOfAny(['^', '/', '*']);
            var host = end > 2 ? line[2..end] : line[2..];
            AddHost(host);
            return;
        }

        if (line.StartsWith('|'))
        {
            line = line.Trim('|');
        }

        if (line.Contains('/') || line.Contains('.') || line.Contains('*'))
        {
            AddUrlContainsRule(line);
        }
    }

    private static bool MatchesAnyHost(string host, IEnumerable<string> candidates)
    {
        return candidates.Select(NormalizeHost)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Any(candidate => HostMatches(host, candidate));
    }

    private static bool HostMatches(string host, string candidate)
    {
        return host.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string value)
    {
        var trimmed = value.Trim().TrimStart('|').TrimEnd('^').Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return trimmed.TrimStart('.').TrimEnd('/');
    }

    private static string NormalizeUrlRule(string rule)
    {
        return rule.Trim()
            .Trim('|')
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("^", string.Empty, StringComparison.Ordinal);
    }
}
