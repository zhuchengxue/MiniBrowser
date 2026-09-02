# Changelog

## 0.7.0

- Replaced the persistent desktop tab strip with a compact mobile-style tab count and card overview.
- Added internal handling for `target=_blank` and `window.open`, plus Ctrl+T, Ctrl+W, and Ctrl+Tab navigation.
- Added lazy restored tabs, delayed inactive-tab suspension, and protection for audio, downloads, and authentication flows.
- Added single-instance activation so launching MiniBrowser again shows the existing window.
- Made edge auto-hide opt-in and fixed the restore/re-arm flicker path.
- Added conservative authentication, verification, and payment bypasses for ad blocking.
- Added per-page blocked-request counts and last-rule diagnostics.
- Added settings version 5 migration, a 20-tab session limit, and isolated test data paths.
- Added local WebView2 fixtures, 42 automated behavior tests, three-run cold-start smoke tests, package data-leak checks, and full process-tree resource measurement.
