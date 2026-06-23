# MiniBrowser

MiniBrowser is a tiny Windows browser shell inspired by MenubarX. It is built with WPF and Microsoft WebView2, and is designed for small mobile-sized windows, tray-first usage, portable data, and lightweight ad blocking.

## Features

- Mobile-sized browser windows with phone/desktop User-Agent switching.
- Minimal top chrome with address bar, back, forward, refresh, and menu controls.
- Tray icon toggle. The window can appear centered above the tray icon.
- Global hotkey: `Ctrl+Shift+Space` shows or hides the first window.
- Multi-window support with restore for window size, position, opacity, topmost, frame, and chrome visibility.
- Site profiles: save phone/desktop mode, ad blocking, size preset, topmost, frame, chrome visibility, and opacity per host.
- Ad blocking with built-in host rules, custom hosts, simplified EasyList parsing, and cosmetic CSS hiding.
- Global whitelist and per-site ad blocking toggle.
- Portable data in `Data/settings.json`, `Data/WebView2`, and `Data/Logs`.
- GitHub Releases update check and portable zip self-update flow.
- Per-user installer script with Start Menu shortcut, Desktop shortcut, and uninstall entry.

## Quick Start

Double-click:

```text
Open-MiniBrowser.cmd
```

The first run builds the portable package and starts:

```text
dist\MiniBrowser-Portable\MiniBrowser.App.exe
```

Requirements:

- Windows 10/11
- Microsoft Edge WebView2 Runtime
- .NET 8 Desktop Runtime x64

## Development

```powershell
.\scripts\Run-Dev.ps1
```

Or run manually:

```powershell
dotnet restore
dotnet build .\MiniBrowser.sln -c Release
dotnet run --project .\src\MiniBrowser.App\MiniBrowser.App.csproj
```

## Build Portable Package

```powershell
.\scripts\Build-Portable.ps1
```

Output:

```text
dist\MiniBrowser-Portable
dist\MiniBrowser-Portable.zip
```

`MiniBrowser-Portable.zip` is the default asset name used by the automatic updater. For a new release, upload this zip to a GitHub Release and use a tag such as `v0.4.9`.

## Build Installer Package

```powershell
.\scripts\Build-Installer.ps1
```

Output:

```text
dist\MiniBrowser-Setup.zip
```

After extracting the setup zip, run:

```text
Install-MiniBrowser.cmd
```

Default install location:

```text
%LOCALAPPDATA%\Programs\MiniBrowser
```

The installer creates Start Menu and Desktop shortcuts, then writes a per-user uninstall entry. Administrator rights are not required.

## Updates

The app menu includes `Check for updates`. By default, MiniBrowser checks once per day:

```text
https://api.github.com/repos/zhuchengxue/MiniBrowser/releases/latest
```

When a newer release contains `MiniBrowser-Portable.zip`, MiniBrowser downloads it, starts an external PowerShell updater, closes itself, replaces app files, preserves `Data`, and restarts.

## Site Profiles

The app menu includes:

- `Save site profile` / `Update site profile`: saves the current window mode for the current host.
- `Clear site profile`: removes the profile for the current host.
- `Ad block: ON/OFF for this site`: toggles ad blocking for the current host.

The Settings window also has a `Site Profiles` tab. Each line uses:

```text
host|mobile|adblock|sizeIndex|topmost|borderless|chrome|opacity
```

Examples:

```text
www.bing.com|True|True|0|True|False|True|1
youtube.com|True|False|1|True|True|False|0.92
```

## Shortcuts

- `Ctrl+Shift+Space`: show/hide first window
- `Ctrl+L`: focus address bar
- `Ctrl+Shift+L`: show controls and focus address bar
- `Ctrl+T`: new window from current page
- `Ctrl+W`: close current window
- `Alt+Left` / `Alt+Right`: back / forward
- `F5` or `Ctrl+R`: reload
- `F8`: clean mode / show controls
- `F9` or `Ctrl+Shift+F`: toggle window frame

## Roadmap

- Broader EasyList syntax coverage.
- GitHub Actions workflow for release publishing.
- Optional MSIX or Inno Setup installer.
