param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\MiniBrowser",
    [switch]$NoDesktopShortcut,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

function Resolve-SourceDir {
    $scriptDir = Split-Path -Parent $MyInvocation.ScriptName
    if (Test-Path -LiteralPath (Join-Path $scriptDir "MiniBrowser.App.exe")) {
        return $scriptDir
    }

    $repoRoot = Split-Path -Parent $scriptDir
    $portableDir = Join-Path $repoRoot "dist\MiniBrowser-Portable"
    if (Test-Path -LiteralPath (Join-Path $portableDir "MiniBrowser.App.exe")) {
        return $portableDir
    }

    throw "MiniBrowser portable files were not found. Run scripts\Build-Portable.ps1 first."
}

function New-Shortcut {
    param(
        [string]$Path,
        [string]$Target,
        [string]$WorkingDirectory,
        [string]$Icon
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $Icon
    $shortcut.Save()
}

$sourceDir = Resolve-SourceDir
$installDir = [System.IO.Path]::GetFullPath($InstallDir)
$exePath = Join-Path $installDir "MiniBrowser.App.exe"
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\MiniBrowser"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "MiniBrowser.lnk"
$startShortcut = Join-Path $startMenuDir "MiniBrowser.lnk"
$uninstallScript = Join-Path $installDir "Uninstall-MiniBrowser.ps1"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniBrowser"

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDir -Force | Where-Object { $_.Name -ne "Data" } | ForEach-Object {
    $destination = Join-Path $installDir $_.Name
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}

$uninstallContent = @"
`$ErrorActionPreference = "Stop"
Get-Process MiniBrowser.App -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath "$startMenuDir" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$desktopShortcut" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$uninstallKey" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$installDir" -Recurse -Force -ErrorAction SilentlyContinue
"@
Set-Content -LiteralPath $uninstallScript -Value $uninstallContent -Encoding UTF8

New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
New-Shortcut -Path $startShortcut -Target $exePath -WorkingDirectory $installDir -Icon $exePath
if (!$NoDesktopShortcut) {
    New-Shortcut -Path $desktopShortcut -Target $exePath -WorkingDirectory $installDir -Icon $exePath
}

New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MiniBrowser"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "0.4.9"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "zhuchengxue"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $exePath
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
New-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null

Write-Output "MiniBrowser installed to:"
Write-Output $installDir

if ($Launch) {
    Start-Process -FilePath $exePath
}
