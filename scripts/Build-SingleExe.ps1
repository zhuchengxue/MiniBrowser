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

$publishDir = Join-Path $repoRoot "dist\MiniBrowser-SingleExe"
$zipPath = Join-Path $repoRoot "dist\MiniBrowser-SingleExe.zip"
$project = Join-Path $repoRoot "src\MiniBrowser.App\MiniBrowser.App.csproj"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"

Get-Process MiniBrowser.App -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like (Join-Path $publishDir "*") } |
    Stop-Process -Force

Start-Sleep -Milliseconds 700

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:AssemblyName=MiniBrowser `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir `
    --configfile $nugetConfig

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Set-Content -LiteralPath (Join-Path $publishDir "README.txt") -Encoding UTF8 -Value @"
MiniBrowser Single EXE
======================

Run:
  MiniBrowser.exe

This package includes the .NET desktop runtime in the executable.
Windows still needs Microsoft Edge WebView2 Runtime, which is already present on most Windows 10/11 systems.

Portable data is saved beside the executable in:
  Data\settings.json
  Data\WebView2
  Data\Logs
"@

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -LiteralPath $publishDir -DestinationPath $zipPath

Write-Output "Single EXE package created:"
Write-Output (Join-Path $publishDir "MiniBrowser.exe")
Write-Output $zipPath
