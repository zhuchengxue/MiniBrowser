param(
    [ValidateRange(1, 10)]
    [int]$Runs = 3,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $repoRoot "dist\MiniBrowser-Portable"
$sourceExe = Join-Path $publishDir "MiniBrowser.App.exe"
if (!(Test-Path -LiteralPath $sourceExe)) {
    throw "Portable app was not found. Run scripts\Build-Portable.ps1 first."
}

$results = @()

function Get-DescendantProcessIds {
    param([int]$RootProcessId)

    $table = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId)
    $result = [Collections.Generic.List[int]]::new()
    $pending = [Collections.Generic.Queue[int]]::new()
    $pending.Enqueue($RootProcessId)
    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        foreach ($child in $table | Where-Object { $_.ParentProcessId -eq $parentId }) {
            $childId = [int]$child.ProcessId
            if (!$result.Contains($childId)) {
                $result.Add($childId)
                $pending.Enqueue($childId)
            }
        }
    }

    return $result.ToArray()
}

for ($run = 1; $run -le $Runs; $run++) {
    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("MiniBrowserColdStart-" + [Guid]::NewGuid().ToString("N"))
    $tempApp = Join-Path $tempRoot "MiniBrowser-Portable"
    $primary = $null
    $secondary = $null
    try {
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        Copy-Item -LiteralPath $publishDir -Destination $tempApp -Recurse -Force
        $dataDirectory = Join-Path $tempApp "Data"
        New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
        $settingsPath = Join-Path $dataDirectory "settings.json"
        @{
            SettingsVersion = 4
            HomeUrl = "https://example.com"
            LastUrl = "https://example.com"
            SearchEngineUrl = "https://www.google.com/search?q={query}"
            PopupPosition = "BottomRight"
            EdgeAutoHideEnabled = $true
            GlobalHotkeyEnabled = $false
            AutoCheckUpdates = $false
            Windows = @(@{
                Id = "cold-start"
                Url = "https://example.com"
                Width = 390
                Height = 844
                Left = -1
                Top = -1
                Opacity = 1.0
                Topmost = $false
                ChromeVisible = $true
            })
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

        $exe = Join-Path $tempApp "MiniBrowser.App.exe"
        $startedAt = [Diagnostics.Stopwatch]::StartNew()
        $primary = Start-Process -FilePath $exe -PassThru
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $loaded = $null
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
            if ($primary.HasExited) {
                throw "Primary process exited during cold start with code $($primary.ExitCode)."
            }

            try {
                $loaded = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
                if ($loaded.SettingsVersion -eq 5 -and $loaded.Windows[0].Left -ge 0 -and $loaded.Windows[0].Top -ge 0) {
                    break
                }
            }
            catch {
                $loaded = $null
            }
        }

        if ($null -eq $loaded -or $loaded.SettingsVersion -ne 5) {
            throw "Settings migration did not complete before timeout."
        }
        if ($loaded.EdgeAutoHideEnabled) {
            throw "Version 5 migration did not disable legacy edge auto hide."
        }

        $secondary = Start-Process -FilePath $exe -PassThru
        if (!$secondary.WaitForExit(5000)) {
            throw "Secondary instance did not exit after signaling the primary."
        }
        if ($primary.HasExited) {
            throw "Primary instance exited when the secondary instance started."
        }

        $startedAt.Stop()
        $results += [pscustomobject]@{
            Run = $run
            Result = "PASS"
            StartupMs = $startedAt.ElapsedMilliseconds
            SettingsVersion = $loaded.SettingsVersion
            EdgeAutoHide = $loaded.EdgeAutoHideEnabled
            Left = [Math]::Round($loaded.Windows[0].Left, 1)
            Top = [Math]::Round($loaded.Windows[0].Top, 1)
            SingleInstance = $true
        }
    }
    finally {
        $processIds = [Collections.Generic.List[int]]::new()
        if ($primary) {
            foreach ($id in @(Get-DescendantProcessIds -RootProcessId $primary.Id)) {
                $processIds.Add($id)
            }
            $processIds.Add($primary.Id)
        }
        if ($secondary) {
            $processIds.Add($secondary.Id)
        }
        for ($index = $processIds.Count - 1; $index -ge 0; $index--) {
            $id = $processIds[$index]
            if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
                Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
            }
        }
        if (Test-Path -LiteralPath $tempRoot) {
            $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
            $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
            if (!$resolvedTempRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove a path outside the system temporary directory: $resolvedTempRoot"
            }
            for ($attempt = 1; $attempt -le 8; $attempt++) {
                try {
                    Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
                    break
                }
                catch {
                    if ($attempt -eq 8) {
                        throw
                    }
                    Start-Sleep -Milliseconds (200 * $attempt)
                }
            }
        }
    }
}

$results
