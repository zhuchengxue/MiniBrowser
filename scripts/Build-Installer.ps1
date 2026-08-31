$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$portableScript = Join-Path $repoRoot "scripts\Build-Portable.ps1"
$distDir = Join-Path $repoRoot "dist"
$portableDir = Join-Path $distDir "MiniBrowser-Portable"
$installerZip = Join-Path $distDir "MiniBrowser-Setup.zip"
$stagingDir = Join-Path $distDir ("MiniBrowser-Setup-" + [Guid]::NewGuid().ToString("N"))
$setupDir = Join-Path $stagingDir "MiniBrowser-Setup"

& $portableScript
if ($LASTEXITCODE -ne 0) {
    throw "Build-Portable.ps1 failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $installerZip) {
    Remove-Item -LiteralPath $installerZip -Force
}

try {
    Copy-Item -LiteralPath $portableDir -Destination $setupDir -Recurse

    $requiredFiles = @(
        (Join-Path $setupDir "MiniBrowser.App.exe"),
        (Join-Path $setupDir "Install-MiniBrowser.cmd"),
        (Join-Path $setupDir "Install-MiniBrowser.ps1"),
        (Join-Path $setupDir "VERSION.txt")
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile)) {
            throw "Installer package is missing required file: $requiredFile"
        }
    }

    Compress-Archive -LiteralPath $setupDir -DestinationPath $installerZip
}
finally {
    if (Test-Path -LiteralPath $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
}

Write-Output "Installer package created:"
Write-Output $installerZip
