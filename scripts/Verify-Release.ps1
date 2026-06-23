param(
    [string]$ExpectedTag = "v0.4.9",
    [string]$Owner = "zhuchengxue",
    [string]$Repository = "MiniBrowser"
)

$ErrorActionPreference = "Stop"

$headers = @{
    "User-Agent" = "MiniBrowser-Release-Verify"
    "Accept" = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$releaseUrl = "https://api.github.com/repos/$Owner/$Repository/releases/latest"
$release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers

if ($release.tag_name -ne $ExpectedTag) {
    throw "Latest release tag is '$($release.tag_name)', expected '$ExpectedTag'."
}

$assetNames = @($release.assets | ForEach-Object { $_.name })
$requiredAssets = @("MiniBrowser-Portable.zip", "MiniBrowser-Setup.zip")
foreach ($asset in $requiredAssets) {
    if ($assetNames -notcontains $asset) {
        throw "Release '$ExpectedTag' is missing asset '$asset'. Found: $($assetNames -join ', ')"
    }
}

[pscustomobject]@{
    Tag = $release.tag_name
    Url = $release.html_url
    Assets = $assetNames -join ", "
}
