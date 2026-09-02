param(
    [int]$WarmupSeconds = 8,
    [ValidateRange(1, 20)]
    [int]$TabCount = 1
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $repoRoot "dist\MiniBrowser-Portable"
$exe = Join-Path $publishDir "MiniBrowser.App.exe"

if (!(Test-Path -LiteralPath $exe)) {
    throw "Portable app was not found. Run scripts\Build-Portable.ps1 first."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MiniBrowserMeasure-" + [Guid]::NewGuid().ToString("N"))
$tempApp = Join-Path $tempRoot "MiniBrowser-Portable"

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
Copy-Item -LiteralPath $publishDir -Destination $tempApp -Recurse -Force
$tempExe = Join-Path $tempApp "MiniBrowser.App.exe"
$process = $null
$childProcesses = @()

function Get-DescendantProcessIds {
    param(
        [int]$RootProcessId,
        [object[]]$ProcessTable
    )

    $result = [System.Collections.Generic.List[int]]::new()
    $pending = [System.Collections.Generic.Queue[int]]::new()
    $pending.Enqueue($RootProcessId)
    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        foreach ($child in $ProcessTable | Where-Object { $_.ParentProcessId -eq $parentId }) {
            if (!$result.Contains([int]$child.ProcessId)) {
                $result.Add([int]$child.ProcessId)
                $pending.Enqueue([int]$child.ProcessId)
            }
        }
    }

    return $result.ToArray()
}

try {
    $dataDirectory = Join-Path $tempApp "Data"
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    $tabs = @(1..$TabCount | ForEach-Object {
        [ordered]@{
            Id = "measure-tab-$_"
            Title = "Measure tab $_"
            Url = "https://example.com/?tab=$_"
        }
    })
    $settings = [ordered]@{
        SettingsVersion = 5
        HomeUrl = "https://example.com"
        LastUrl = "https://example.com"
        SearchEngineUrl = "https://www.google.com/search?q={query}"
        PopupPosition = "BottomRight"
        EdgeAutoHideEnabled = $false
        GlobalHotkeyEnabled = $false
        AutoCheckUpdates = $false
        AdBlockEnabled = $true
        Windows = @([ordered]@{
            Id = "measure-window"
            Url = $tabs[0].Url
            Width = 390
            Height = 844
            Left = -1
            Top = -1
            Opacity = 1.0
            Topmost = $false
            ChromeVisible = $true
            AdBlockEnabled = $true
            ActiveTabId = $tabs[0].Id
            Tabs = $tabs
        })
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataDirectory "settings.json") -Encoding UTF8

    $process = Start-Process -FilePath $tempExe -PassThru
    Start-Sleep -Seconds $WarmupSeconds

    $app = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    $processTable = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId)
    $descendantIds = @(Get-DescendantProcessIds -RootProcessId $process.Id -ProcessTable $processTable)
    $childProcesses = @($descendantIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    $allProcesses = @($app) + @($childProcesses) | Where-Object { $_ }
    $details = @($allProcesses |
        Where-Object { $_ } |
        Select-Object ProcessName,
            Id,
            @{Name = "WorkingSetMB"; Expression = { [Math]::Round($_.WorkingSet64 / 1MB, 1) } },
            @{Name = "PrivateMB"; Expression = { [Math]::Round($_.PrivateMemorySize64 / 1MB, 1) } })

    Write-Output "Configuration: Tabs=$TabCount WarmupSeconds=$WarmupSeconds"
    $details
    [pscustomobject]@{
        ProcessName = "TOTAL"
        Id = "-"
        WorkingSetMB = [Math]::Round(($allProcesses | Measure-Object WorkingSet64 -Sum).Sum / 1MB, 1)
        PrivateMB = [Math]::Round(($allProcesses | Measure-Object PrivateMemorySize64 -Sum).Sum / 1MB, 1)
    }
}
finally {
    foreach ($child in $childProcesses) {
        if ($child -and (Get-Process -Id $child.Id -ErrorAction SilentlyContinue)) {
            Stop-Process -Id $child.Id -Force
        }
    }

    if ($process -and (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $process.Id -Force
    }

    if (Test-Path -LiteralPath $tempRoot) {
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force
                break
            }
            catch {
                if ($attempt -eq 5) {
                    Write-Warning "Could not remove temporary measurement directory: $tempRoot"
                    break
                }

                Start-Sleep -Milliseconds (300 * $attempt)
            }
        }
    }
}
