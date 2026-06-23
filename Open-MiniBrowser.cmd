@echo off
setlocal

set "ROOT=%~dp0"
set "DOTNET=C:\tmp\dotnet8sdk\dotnet.exe"
set "PROJECT=%ROOT%src\MiniBrowser.App\MiniBrowser.App.csproj"
set "SOLUTION=%ROOT%MiniBrowser.sln"
set "CONFIG=%ROOT%NuGet.Config"
set "EXE=%ROOT%dist\MiniBrowser-Portable\MiniBrowser.App.exe"

if not exist "%DOTNET%" set "DOTNET=dotnet"

set "DOTNET_CLI_HOME=%ROOT%.dotnet-home"
set "NUGET_PACKAGES=%ROOT%.nuget-packages"
set "APPDATA=%ROOT%.appdata"
set "LOCALAPPDATA=%ROOT%.localappdata"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo Starting MiniBrowser...

if not exist "%EXE%" (
  echo Building portable MiniBrowser first...
  powershell -ExecutionPolicy Bypass -File "%ROOT%scripts\Build-Portable.ps1"
  if errorlevel 1 (
    echo.
    echo Portable build failed. Please check the error above.
    pause
    exit /b 1
  )
)

start "MiniBrowser" "%EXE%"
exit /b 0
