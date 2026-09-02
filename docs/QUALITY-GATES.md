# MiniBrowser Quality Gates

This document is the release contract for MiniBrowser. A release is blocked when
any required item is missing, failing, or only verified against an old build.

## Acceptance matrix

| Area | Required behavior | Automated evidence | Release evidence |
| --- | --- | --- | --- |
| Startup | First launch opens at the configured bottom anchor | placement unit tests | clean-profile UI run |
| Tray | One click shows, the next click hides | window-state integration test | three repeated UI runs |
| Hotkey | Ctrl+Shift+Space toggles and focuses the address bar | command integration test | real EXE UI run |
| Address | Ctrl+L always focuses and selects; one Enter navigates once | navigation integration test | local test-site UI run |
| Tabs | No persistent tab strip; overview owns add, close, and switch | tab lifecycle tests | real EXE UI run |
| Popups | target=_blank and window.open create an internal tab | WebView2 integration test | local test-site UI run |
| Sessions | The last tab becomes a home tab; legacy windows merge safely | settings and tab tests | restart UI run |
| Ad block | Known ad rules block while login and verification hosts bypass | ad-block unit tests | compatibility fixture run |
| Resources | Inactive safe tabs suspend and resume without losing the URL | lifecycle integration test | measured five-tab run |
| Packaging | Portable, single EXE, and installer contain no user Data | package verifier | clean-directory smoke run |

## Historical regressions

Every fixed item below must retain a regression test:

- startup unexpectedly centered
- popup not aligned with the selected taskbar anchor
- edge auto-hide flicker
- tray or global hotkey failing to toggle
- address bar not focused after showing
- Ctrl+L failing while WebView2 owns keyboard focus
- Enter requiring a second press or navigating twice
- Google home rewritten to google.cn/m
- search results opening an external browser window
- borderless mode with no recovery path
- hidden controls that cannot be restored
- navigation buttons starving the address bar
- closing the last tab exiting the application
- ad blocking causing broad verification challenges
- non-finite settings values breaking JSON serialization
- release packages containing local user data
- UI tests accidentally targeting an older process

## Test rules

1. Reproduce a defect with a failing test before changing production behavior.
2. Core tests use the local test site and never depend on a public search engine.
3. UI tests start with no MiniBrowser process and verify the launched binary path and version.
4. Wait for observable state; fixed sleeps are not accepted as success criteria.
5. UI release tests run three times at 100%, 125%, and 150% display scaling.
6. Release builds require zero compiler warnings and zero errors.
7. Packages are tested from a fresh temporary directory and must not contain `Data`.

Run the local package gate with:

```powershell
.\scripts\Test-ReleasePackages.ps1
.\scripts\Test-ColdStart.ps1 -Runs 3
```
