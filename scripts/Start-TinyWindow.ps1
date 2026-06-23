param(
    [string]$Url = "https://www.bing.com",
    [int]$Width = 390,
    [int]$Height = 844
)

$edgeCandidates = @(
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:LocalAppData\Microsoft\Edge\Application\msedge.exe"
)

$edge = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $edge) {
    Write-Error "Microsoft Edge was not found. Install Edge or build the WPF/WebView2 app after installing the .NET SDK."
    exit 1
}

$userData = Join-Path $env:TEMP "MiniBrowserEdgeProfile"
New-Item -ItemType Directory -Path $userData -Force | Out-Null

$arguments = @(
    "--app=$Url",
    "--window-size=$Width,$Height",
    "--user-data-dir=$userData",
    "--disable-features=Translate",
    "--no-first-run"
)

Start-Process -FilePath $edge -ArgumentList $arguments
Write-Output "Started tiny browser window: $Url ($Width x $Height)"
