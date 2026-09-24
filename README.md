<p align="center"><img src="branding/Haulix_ETS2_Logger.png" alt="HAULIX – ETS2 Logger" width="520"></p>

<p align="center"><b>Telemetry, logbook, road map and fleet insights for Euro Truck Simulator 2 – on your PC, without an account.</b></p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download the latest version</b></a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="#deutsch">Deutsch</a>
</p>

---

## Features

- **Live telemetry** – speed, RPM, gear, fuel, AdBlue, wear, lights, job and trailer, via the free SCS SDK plugin (installed by the setup).
- **Automatic logbook** – every delivery with route, income, XP, fuel, damage and a **driving score** (0–100); CSV export and share cards.
- **Road map & navigation** – the full ETS2 map (all map DLCs) in HAULIX's own style: streets you have driven, city areas, country names. Routes to your job's destination; HAULIX follows route changes you make in the in-game GPS.
- **Real-time ETA** – arrival in real minutes and clock time, plus the game ETA and your deadline buffer.
- **In-game notifications & HUD** – milestones, deadline, fuel, damage, rest and route changes as cards over the game; a configurable HUD bar (position, size, opacity, fields); optional voice announcements.
- **Your company** – trucks, trailers, garages, AI drivers, finances and statistics read from your save.
- **Achievements** – 16 bronze, silver and gold achievements from your career.
- **TruckersMP** – inactivity warning before the AFK kick; an optional anti-AFK chat message (against the TruckersMP rules – off by default, use at your own risk).
- **German & English** – detected from Windows, switchable any time.
- **Updates from GitHub** – HAULIX checks this repository's releases and updates itself; no HAULIX server involved.

Coming later: VTC features (find a VTC, company pages, events & convoys, job board, leaderboards), live map, cloud sync, Discord status, ATS support.

## Install

1. Download **`HAULIX-Setup-<version>.exe`** from the [latest release](../../releases/latest).
2. Run it. HAULIX installs for your Windows user only (no admin rights) and installs the telemetry plugin into ETS2.
3. Start ETS2, accept the *Advanced SDK features* prompt once, and HAULIX switches to **LIVE**.

The setup is not code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"* – click **More info → Run anyway**.

Notifications and the HUD appear over the game when ETS2 runs in **borderless fullscreen** or window mode; HAULIX can switch that for you (Settings → Notifications).

## Privacy

Everything stays on your PC (`%LOCALAPPDATA%\Haulix`). The only network request HAULIX makes is the optional update check against this repository's public releases (Settings → About).

## Build from source

Requirements: Windows 10/11, the .NET 10 SDK and the Edge WebView2 Runtime (built into Windows 11).

```bash
dotnet build Haulix.slnx
dotnet test Haulix.slnx
dotnet run --project src/Haulix.App
```

In Debug builds the UI is served straight from `src/Haulix.App/wwwroot`, so UI changes only need **F5** in the window. `tools/dev/serve.ps1` previews the UI in a browser with sample data (`http://localhost:5178`).

### Releasing

1. Raise `<Version>` in `src/Haulix.App`, `src/Haulix.Core` and `src/Haulix.Installer` (e.g. `0.0.6-beta`).
2. Add the version to `src/Haulix.App/wwwroot/changelog.json` (English + German). `CHANGELOG.md`, the release notes and the in-app *What's new* window are generated from it.
3. Start HAULIX once so the road map for the current game version is built (it is bundled into the setup).
4. Run:

```bash
powershell -ExecutionPolicy Bypass -File tools\release\publish-github.ps1
```

This builds the setup and source zip, commits, tags `v<version>` and creates the GitHub release. Installed copies find it on their next update check.

## Project layout

| Path | What |
|---|---|
| `src/Haulix.Core` | Backend: SII save decoding, telemetry, SQLite logbook, road map builder (TruckLib), router, ETA, notifications, achievements |
| `src/Haulix.App` | WinForms + WebView2 host, overlay/HUD windows, the UI in `wwwroot/` (HTML/CSS/JS, no build step) |
| `src/Haulix.Installer` | The custom setup (.NET Framework 4.8) |
| `tests/Haulix.Core.Tests` | xUnit tests |
| `tools/release` | Build and publish scripts |
| `branding` | Logos and artwork |

## Legal

HAULIX is an unofficial fan project and is not affiliated with or endorsed by SCS Software or TruckersMP. *Euro Truck Simulator 2* is a trademark of SCS Software.

- The road map bundled in the setup is derived from ETS2 game files, which are © SCS Software.
- The anti-AFK message breaks the TruckersMP rules ("bypassing the server-sided auto kick system") and can lead to bans; it is off by default.
- HAULIX is licensed under the **GNU GPL v2** (see [LICENSE](LICENSE)) because it uses [TruckLib](https://github.com/sk-zk/TruckLib) (GPL-2.0). Third-party components: scs-sdk-plugin (MIT), Leaflet (BSD-2), uPlot (MIT), Lucide icons (ISC), Inter / Barlow Condensed / JetBrains Mono (OFL).

---

## Deutsch

**HAULIX** ist ein Telemetrie-, Fahrtenbuch- und Karten-Tool für Euro Truck Simulator 2 – lokal auf deinem PC, ohne Konto.

**Installation:** Lade `HAULIX-Setup-<version>.exe` aus dem [neuesten Release](../../releases/latest) herunter und starte es. Das Setup installiert HAULIX nur für deinen Windows-Benutzer (ohne Adminrechte) und richtet das Telemetrie-Plugin in ETS2 ein. Warnt Windows SmartScreen, klicke auf *Weitere Informationen → Trotzdem ausführen*.

**Updates:** HAULIX prüft die Releases dieses Repositorys und aktualisiert sich auf Wunsch selbst – ganz ohne eigenen Server.

Alle Änderungen stehen im [Changelog](CHANGELOG.md) und in HAULIX unter *Einstellungen → Über → Neuigkeiten*.
