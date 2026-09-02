param(
    [string]$DistDirectory
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($DistDirectory)) {
    $DistDirectory = Join-Path $repoRoot "dist"
}

$appInfoPath = Join-Path $repoRoot "src\MiniBrowser.App\Infrastructure\AppInfo.cs"
$appInfo = Get-Content -LiteralPath $appInfoPath -Raw
if ($appInfo -notmatch 'Version\s*=\s*"([^"]+)"') {
    throw "Could not read MiniBrowser version from $appInfoPath"
}

$expectedVersion = $Matches[1]
$portableDirectory = Join-Path $DistDirectory "MiniBrowser-Portable"
$singleDirectory = Join-Path $DistDirectory "MiniBrowser-SingleExe"
$portableZip = Join-Path $DistDirectory "MiniBrowser-Portable.zip"
$singleZip = Join-Path $DistDirectory "MiniBrowser-SingleExe.zip"
$setupZip = Join-Path $DistDirectory "MiniBrowser-Setup.zip"

$requiredFiles = @(
    (Join-Path $portableDirectory "MiniBrowser.App.exe"),
    (Join-Path $portableDirectory "VERSION.txt"),
    (Join-Path $singleDirectory "MiniBrowser.exe"),
    (Join-Path $singleDirectory "VERSION.txt"),
    $portableZip,
    $singleZip,
    $setupZip
)
foreach ($path in $requiredFiles) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release artifact is missing: $path"
    }
}

$portableVersion = (Get-Content -LiteralPath (Join-Path $portableDirectory "VERSION.txt") -Raw).Trim()
$singleVersion = (Get-Content -LiteralPath (Join-Path $singleDirectory "VERSION.txt") -Raw).Trim()
if ($portableVersion -ne $expectedVersion -or $singleVersion -ne $expectedVersion) {
    throw "Version mismatch. Source=$expectedVersion Portable=$portableVersion Single=$singleVersion"
}

if (Test-Path -LiteralPath (Join-Path $portableDirectory "Data")) {
    throw "Portable directory contains user Data."
}
if (Test-Path -LiteralPath (Join-Path $singleDirectory "Data")) {
    throw "Single EXE directory contains user Data."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($zipPath in @($portableZip, $singleZip, $setupZip)) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $unsafeEntries = @($archive.Entries | Where-Object {
            $normalized = $_.FullName.Replace('\', '/')
            $normalized -match '(^|/)Data(/|$)' -or
            $normalized -match '(^|/)(settings\.json|Cookies|WebView2)(/|$)'
        })
        if ($unsafeEntries.Count -gt 0) {
            throw "Package '$zipPath' contains user data entries: $($unsafeEntries.FullName -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }
}

$artifacts = @($portableZip, $singleZip, $setupZip, (Join-Path $singleDirectory "MiniBrowser.exe")) |
    ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [pscustomobject]@{
            File = $item.FullName
            SizeMB = [Math]::Round($item.Length / 1MB, 2)
            SHA256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        }
    }

[pscustomobject]@{
    Result = "PASS"
    Version = $expectedVersion
    ArtifactCount = $artifacts.Count
    UserDataFound = $false
}
$artifacts
