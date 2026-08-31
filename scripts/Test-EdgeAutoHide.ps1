param(
    [int]$WarmupSeconds = 8,
    [int]$HideTimeoutSeconds = 6,
    [switch]$KeepTempOnFailure
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $repoRoot "dist\MiniBrowser-Portable"
$exe = Join-Path $publishDir "MiniBrowser.App.exe"

if (!(Test-Path -LiteralPath $exe)) {
    throw "Portable app was not found. Run scripts\Build-Portable.ps1 first."
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class MiniBrowserWin32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
"@

function Get-WindowForProcess {
    param([int]$ProcessId)

    $script:foundWindow = [IntPtr]::Zero
    [MiniBrowserWin32]::EnumWindows({
        param([IntPtr]$hWnd, [IntPtr]$lParam)

        [uint32]$windowProcessId = 0
        [MiniBrowserWin32]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId) | Out-Null
        if ($windowProcessId -eq [uint32]$lParam.ToInt32() -and [MiniBrowserWin32]::IsWindowVisible($hWnd)) {
            $script:foundWindow = $hWnd
            return $false
        }

        return $true
    }, [IntPtr]$ProcessId) | Out-Null

    return $script:foundWindow
}

function Get-Rect {
    param([IntPtr]$Handle)

    $rect = New-Object MiniBrowserWin32+Rect
    if (![MiniBrowserWin32]::GetWindowRect($Handle, [ref]$rect)) {
        throw "Could not read MiniBrowser window bounds."
    }

    return $rect
}

function Get-AppLogTail {
    param([string]$AppDirectory)

    $logPath = Join-Path $AppDirectory "Data\Logs\MiniBrowser.log"
    if (!(Test-Path -LiteralPath $logPath)) {
        return "log=missing"
    }

    $lines = Get-Content -LiteralPath $logPath -Tail 12
    if (!$lines) {
        return "log=empty"
    }

    return "log=" + (($lines -join " | ") -replace "`r|`n", " ")
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MiniBrowserEdgeTest-" + [Guid]::NewGuid().ToString("N"))
$tempApp = Join-Path $tempRoot "MiniBrowser-Portable"
$process = $null
$testFailed = $false
$childProcesses = @()
$originalCursor = New-Object MiniBrowserWin32+Point
[MiniBrowserWin32]::GetCursorPos([ref]$originalCursor) | Out-Null

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
Copy-Item -LiteralPath $publishDir -Destination $tempApp -Recurse -Force

$dataDir = Join-Path $tempApp "Data"
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
@"
{
  "HomeUrl": "https://www.google.com/ncr",
  "LastUrl": "https://www.google.com/ncr",
  "SearchEngineUrl": "https://www.google.com/search?q={query}",
  "PopupPosition": "BottomRight",
  "EdgeAutoHideEnabled": true,
  "GlobalHotkeyEnabled": false,
  "LowMemoryMode": true,
  "AutoCheckUpdates": false,
  "AdBlockEnabled": true,
  "Windows": [
    {
      "Id": "edge-test",
      "Url": "https://www.google.com/ncr",
      "Width": 390,
      "Height": 760,
      "Left": -1,
      "Top": -1,
      "Opacity": 1.0,
      "Topmost": true,
      "MobileMode": true,
      "ChromeVisible": true,
      "AdBlockEnabled": true,
      "SizePresetIndex": 0
    }
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $dataDir "settings.json") -Encoding UTF8

try {
    $process = Start-Process -FilePath (Join-Path $tempApp "MiniBrowser.App.exe") -PassThru
    Start-Sleep -Seconds $WarmupSeconds

    $handle = [IntPtr]::Zero
    for ($attempt = 1; $attempt -le 40; $attempt++) {
        $handle = Get-WindowForProcess -ProcessId $process.Id
        if ($handle -ne [IntPtr]::Zero) {
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if ($handle -eq [IntPtr]::Zero) {
        throw "Could not find a visible MiniBrowser window for edge auto-hide test."
    }

    $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $width = 390
    $height = [Math]::Min(760, $work.Height - 80)
    $left = $work.Right - $width
    $top = $work.Top + 40
    $flags = 0x0004 -bor 0x0010

    [MiniBrowserWin32]::SetWindowPos($handle, [IntPtr]::Zero, $left, $top, $width, $height, $flags) | Out-Null
    [MiniBrowserWin32]::SetCursorPos($left + [int]($width / 2), $top + 48) | Out-Null
    Start-Sleep -Milliseconds 700
    [MiniBrowserWin32]::SetCursorPos($work.Left + 50, $work.Top + 50) | Out-Null

    $hidden = $false
    $hiddenRect = $null
    $deadline = (Get-Date).AddSeconds($HideTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $rect = Get-Rect -Handle $handle
        if ([Math]::Abs($rect.Left - ($work.Right - 4)) -le 8) {
            $hidden = $true
            $hiddenRect = $rect
            break
        }
    }

    if (!$hidden) {
        $rect = Get-Rect -Handle $handle
        $logTail = Get-AppLogTail -AppDirectory $tempApp
        throw "Window did not auto-hide on the right edge. Rect=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom) Size=$($rect.Width)x$($rect.Height) Work=$($work.Left),$($work.Top),$($work.Right),$($work.Bottom) ExpectedLeftNear=$($work.Right - 4). $logTail"
    }

    $samples = @()
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Milliseconds 250
        $samples += (Get-Rect -Handle $handle)
    }

    $minLeft = ($samples | Measure-Object -Property Left -Minimum).Minimum
    $maxLeft = ($samples | Measure-Object -Property Left -Maximum).Maximum
    if (($maxLeft - $minLeft) -gt 2) {
        throw "Window flickered while hidden. Hidden left range: $minLeft..$maxLeft"
    }

    [MiniBrowserWin32]::SetCursorPos($work.Right - 2, $hiddenRect.Top + [Math]::Max(20, [int]($hiddenRect.Height / 2))) | Out-Null
    $revealCursor = New-Object MiniBrowserWin32+Point
    [MiniBrowserWin32]::GetCursorPos([ref]$revealCursor) | Out-Null
    $revealSamples = @()
    $revealedRect = $null
    for ($i = 0; $i -lt 24; $i++) {
        Start-Sleep -Milliseconds 125
        $sample = Get-Rect -Handle $handle
        $revealSamples += $sample
        if ($sample.Left -le ($work.Right - 120) -and $sample.Right -le ($work.Right + 8)) {
            $revealedRect = $sample
            break
        }
    }

    if ($null -eq $revealedRect) {
        $revealedRect = $revealSamples[-1]
    }

    if ($revealedRect.Right -gt ($work.Right + 8) -or $revealedRect.Left -gt ($work.Right - 120)) {
        $logTail = Get-AppLogTail -AppDirectory $tempApp
        throw "Window did not reveal from the visible edge strip. Left=$($revealedRect.Left), Right=$($revealedRect.Right), Cursor=$($revealCursor.X),$($revealCursor.Y). $logTail"
    }

    Start-Sleep -Seconds 1
    $settledRect = Get-Rect -Handle $handle
    if ($settledRect.Left -gt ($work.Right - 120)) {
        throw "Window flickered back to hidden after reveal. RevealedLeft=$($revealedRect.Left), SettledLeft=$($settledRect.Left), Cursor=$($revealCursor.X),$($revealCursor.Y)."
    }

    [pscustomobject]@{
        Result = "PASS"
        HiddenLeft = $hiddenRect.Left
        StableLeftMin = $minLeft
        StableLeftMax = $maxLeft
        RevealedLeft = $revealedRect.Left
        RevealedRight = $revealedRect.Right
    }
}
catch {
    $testFailed = $true
    throw
}
finally {
    [MiniBrowserWin32]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null

    if ($process) {
        $childProcesses = @(Get-CimInstance Win32_Process |
            Where-Object { $_.ParentProcessId -eq $process.Id } |
            ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })

        foreach ($child in $childProcesses) {
            if ($child -and (Get-Process -Id $child.Id -ErrorAction SilentlyContinue)) {
                Stop-Process -Id $child.Id -Force
            }
        }

        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $process.Id -Force
        }
    }

    if ($KeepTempOnFailure -and $testFailed) {
        Write-Warning "Keeping temporary edge test directory: $tempRoot"
    }
    elseif (Test-Path -LiteralPath $tempRoot) {
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force
                break
            }
            catch {
                if ($attempt -eq 5) {
                    Write-Warning "Could not remove temporary edge test directory: $tempRoot"
                    break
                }

                Start-Sleep -Milliseconds (300 * $attempt)
            }
        }
    }
}
