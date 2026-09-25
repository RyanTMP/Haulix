# Developing HAULIX

## Build from source

Requirements: Windows 10/11, the .NET 10 SDK and the Edge WebView2 Runtime (built into Windows 11). Players don't need any of this – the release is self-contained and its setup installs WebView2 when it is missing.

```bash
dotnet build Haulix.slnx
dotnet test Haulix.slnx
dotnet run --project src/Haulix.App
```

In Debug builds the UI is served straight from `src/Haulix.App/wwwroot`, so UI changes only need **F5** in the window. `tools/dev/serve.ps1` previews the UI in a browser with sample data (`http://localhost:5178`).

## Releasing

1. Raise `<Version>` in `src/Haulix.App`, `src/Haulix.Core` and `src/Haulix.Installer` (e.g. `0.0.7-beta`).
2. Add the version to `src/Haulix.App/wwwroot/changelog.json` (English + German). `CHANGELOG.md`, the release notes and the in-app *What's new* window are generated from it.
3. Start HAULIX once so the road map for the current game version is built (it is bundled into the setup).
4. Run:

```bash
powershell -ExecutionPolicy Bypass -File tools\release\publish-github.ps1
```

This builds the setup (`dist\Haulix.exe`) and the source zip, commits, tags `v<version>` and creates the GitHub release. Installed copies find it on their next update check. Code signing is prepared in `tools/release/sign.ps1` / `signing.json`.

## Project layout

| Path | What |
|---|---|
| `src/Haulix.Core` | Backend: SII save decoding, telemetry, SQLite logbook, road map builder (TruckLib), router, ETA, notifications, achievements, online groundwork |
| `src/Haulix.App` | WinForms + WebView2 host, overlay/HUD windows, voices, the UI in `wwwroot/` (HTML/CSS/JS, no build step) |
| `src/Haulix.Installer` | The custom setup (.NET Framework 4.8, part of Windows 10/11) |
| `tests/Haulix.Core.Tests` | xUnit tests |
| `tools/release` | Build, sign and publish scripts |
| `docs/online-api.md` | Draft contract of the future HAULIX online service |
| `branding` | Logos and artwork |
