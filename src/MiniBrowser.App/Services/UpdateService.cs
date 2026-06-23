using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using MiniBrowser.App.Infrastructure;

namespace MiniBrowser.App.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases/latest";
        using var response = await Client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return UpdateCheckResult.Unavailable($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var releaseUrl = root.GetProperty("html_url").GetString() ?? $"https://github.com/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases";
        var version = NormalizeVersion(tagName);
        var current = Version.Parse(AppInfo.Version);

        if (version <= current)
        {
            return UpdateCheckResult.Current(tagName, releaseUrl);
        }

        var asset = FindPortableAsset(root);
        if (asset is null)
        {
            return UpdateCheckResult.Available(tagName, releaseUrl, null);
        }

        return UpdateCheckResult.Available(tagName, releaseUrl, asset);
    }

    public async Task<string> DownloadAsync(UpdateAsset asset, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RuntimePaths.UpdatesDirectory);
        var zipPath = Path.Combine(RuntimePaths.UpdatesDirectory, AppInfo.PortableAssetName);
        using var response = await Client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(zipPath);
        var buffer = new byte[81920];
        long readTotal = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (total is > 0)
            {
                progress?.Report((double)readTotal / total.Value);
            }
        }

        return zipPath;
    }

    public string PrepareUpdaterScript(string zipPath)
    {
        var scriptPath = Path.Combine(RuntimePaths.UpdatesDirectory, "ApplyUpdate.ps1");
        var appDirectory = RuntimePaths.AppDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var processId = Environment.ProcessId;
        var escapedZip = EscapePowerShell(zipPath);
        var escapedApp = EscapePowerShell(appDirectory);
        var script = $$"""
$ErrorActionPreference = "Stop"
$zipPath = '{{escapedZip}}'
$appDir = '{{escapedApp}}'
$processId = {{processId}}
$staging = Join-Path (Split-Path -Parent $zipPath) 'staging'

try {
    Wait-Process -Id $processId -Timeout 60 -ErrorAction SilentlyContinue
} catch {
}

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $staging -Force

$payload = Join-Path $staging 'MiniBrowser-Portable'
if (!(Test-Path -LiteralPath $payload)) {
    $payload = $staging
}

Get-ChildItem -LiteralPath $payload -Force | Where-Object { $_.Name -ne 'Data' } | ForEach-Object {
    $destination = Join-Path $appDir $_.Name
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}

Start-Process -FilePath (Join-Path $appDir 'MiniBrowser.App.exe')
""";
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    public void LaunchUpdater(string scriptPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(info);
    }

    private static UpdateAsset? FindPortableAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in assets.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            if (!string.Equals(name, AppInfo.PortableAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = item.GetProperty("browser_download_url").GetString();
            return string.IsNullOrWhiteSpace(downloadUrl) ? null : new UpdateAsset(name!, downloadUrl);
        }

        return null;
    }

    private static Version NormalizeVersion(string tagName)
    {
        var value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        return Version.TryParse(value, out var version) ? version : new Version(0, 0, 0);
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ProductName}/{AppInfo.Version}");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
}

public sealed record UpdateAsset(string Name, string DownloadUrl);

public sealed record UpdateCheckResult(
    bool IsAvailable,
    bool IsUnavailable,
    string VersionTag,
    string ReleaseUrl,
    UpdateAsset? Asset,
    string? Error)
{
    public static UpdateCheckResult Current(string versionTag, string releaseUrl)
    {
        return new UpdateCheckResult(false, false, versionTag, releaseUrl, null, null);
    }

    public static UpdateCheckResult Available(string versionTag, string releaseUrl, UpdateAsset? asset)
    {
        return new UpdateCheckResult(true, false, versionTag, releaseUrl, asset, null);
    }

    public static UpdateCheckResult Unavailable(string error)
    {
        return new UpdateCheckResult(false, true, string.Empty, $"https://github.com/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases", null, error);
    }
}
