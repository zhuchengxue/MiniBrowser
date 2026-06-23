$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$localDotnet = "C:\tmp\dotnet8sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget-packages"
$env:APPDATA = Join-Path $repoRoot ".appdata"
$env:LOCALAPPDATA = Join-Path $repoRoot ".localappdata"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnet run --project (Join-Path $repoRoot "src\MiniBrowser.App\MiniBrowser.App.csproj") --configfile (Join-Path $repoRoot "NuGet.Config")
