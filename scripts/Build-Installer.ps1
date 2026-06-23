$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$portableScript = Join-Path $repoRoot "scripts\Build-Portable.ps1"
$distDir = Join-Path $repoRoot "dist"
$portableDir = Join-Path $distDir "MiniBrowser-Portable"
$installerZip = Join-Path $distDir "MiniBrowser-Setup.zip"

& $portableScript
if ($LASTEXITCODE -ne 0) {
    throw "Build-Portable.ps1 failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $installerZip) {
    Remove-Item -LiteralPath $installerZip -Force
}

Compress-Archive -LiteralPath $portableDir -DestinationPath $installerZip
Write-Output "Installer package created:"
Write-Output $installerZip
