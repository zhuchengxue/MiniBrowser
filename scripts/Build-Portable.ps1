$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$localDotnet = "C:\tmp\dotnet8sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget-packages"
$env:APPDATA = Join-Path $repoRoot ".appdata"
$env:LOCALAPPDATA = Join-Path $repoRoot ".localappdata"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$publishDir = Join-Path $repoRoot "dist\MiniBrowser-Portable"
$zipPath = Join-Path $repoRoot "dist\MiniBrowser-Portable.zip"
$project = Join-Path $repoRoot "src\MiniBrowser.App\MiniBrowser.App.csproj"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$appInfoPath = Join-Path $repoRoot "src\MiniBrowser.App\Infrastructure\AppInfo.cs"

function Get-AppVersion {
    $appInfo = Get-Content -LiteralPath $appInfoPath -Raw
    if ($appInfo -notmatch 'Version\s*=\s*"([^"]+)"') {
        throw "Could not read MiniBrowser version from $appInfoPath"
    }

    return $Matches[1]
}

$appVersion = Get-AppVersion

Get-Process MiniBrowser.App -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like (Join-Path $publishDir "*") } |
    Stop-Process -Force

Start-Sleep -Milliseconds 700

if (Test-Path -LiteralPath $publishDir) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $publishDir -Recurse -Force
            break
        } catch {
            if ($attempt -eq 5) {
                throw
            }

            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir `
    --configfile $nugetConfig

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeDataDir = Join-Path $publishDir "Data"
if (Test-Path -LiteralPath $runtimeDataDir) {
    Remove-Item -LiteralPath $runtimeDataDir -Recurse -Force
}

$launcher = @"
@echo off
setlocal
start "" "%~dp0MiniBrowser.App.exe"
"@

Set-Content -LiteralPath (Join-Path $publishDir "MiniBrowser.cmd") -Value $launcher -Encoding ASCII

$readme = @"
MiniBrowser Portable
====================

Version:
  $appVersion

Run:
  MiniBrowser.cmd

Requirements:
  - Windows 10/11
  - Microsoft Edge WebView2 Runtime
  - .NET 8 Desktop Runtime x64

Portable data:
  Settings are stored in .\Data\settings.json next to this app.
  A settings backup is stored in .\Data\settings.backup.json.
  Logs are written to .\Data\Logs\MiniBrowser.log.

Shortcuts:
  Ctrl+Shift+Space  Show/hide first window
  Ctrl+L            Focus address bar
  Ctrl+Shift+L      Show controls and focus address bar
  Ctrl+T            New window from current page
  Ctrl+W            Close this window
  Alt+Left/Right    Back/forward
  F5                Reload
  Ctrl+R            Reload
  F8                Clean mode / show controls
  F9                Toggle window frame

Behavior:
  Snap the window to a screen edge and move the mouse away to auto-hide it.
  Automatic update checks are disabled by default for faster startup.
"@

Set-Content -LiteralPath (Join-Path $publishDir "README.txt") -Value $readme -Encoding UTF8
Set-Content -LiteralPath (Join-Path $publishDir "VERSION.txt") -Value $appVersion -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\Install-MiniBrowser.ps1") -Destination (Join-Path $publishDir "Install-MiniBrowser.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\Install-MiniBrowser.cmd") -Destination (Join-Path $publishDir "Install-MiniBrowser.cmd") -Force

if (Test-Path -LiteralPath (Join-Path $publishDir "Data")) {
    throw "Portable publish directory must not include runtime Data."
}

$debugFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -Force -File -Filter "*.pdb"
if ($debugFiles) {
    throw "Portable publish directory must not include debug symbols: $($debugFiles[0].FullName)"
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -LiteralPath $publishDir -DestinationPath $zipPath

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $forbiddenEntry = $zip.Entries | Where-Object {
        $_.FullName -like "*/Data/*" -or $_.FullName -like "*.pdb"
    } | Select-Object -First 1
    if ($forbiddenEntry) {
        throw "Portable zip contains forbidden entry: $($forbiddenEntry.FullName)"
    }
}
finally {
    $zip.Dispose()
}

Write-Output "Portable package created:"
Write-Output $publishDir
Write-Output $zipPath
