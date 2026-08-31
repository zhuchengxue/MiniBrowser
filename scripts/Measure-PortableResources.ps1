param(
    [int]$WarmupSeconds = 8
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

try {
    $process = Start-Process -FilePath $tempExe -PassThru
    Start-Sleep -Seconds $WarmupSeconds

    $app = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    $childProcesses = @(Get-CimInstance Win32_Process |
        Where-Object { $_.ParentProcessId -eq $process.Id } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })

    @($app) + @($childProcesses) |
        Where-Object { $_ } |
        Select-Object ProcessName,
            Id,
            @{Name = "WorkingSetMB"; Expression = { [Math]::Round($_.WorkingSet64 / 1MB, 1) } },
            @{Name = "PrivateMB"; Expression = { [Math]::Round($_.PrivateMemorySize64 / 1MB, 1) } }
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
