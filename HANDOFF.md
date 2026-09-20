# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 3)._

## State

- Repo: `github.com/mobread/MobreadModernDock` (detached from the Cedro fork). Local: `C:\Users\micha\claude-projects\MobreadModernDock`.
- `main` and `feature/mobread-customizations` are kept in sync by no-ff merges; `main` is the default branch and renders the README.
- 78/78 tests. TFM is now `net9.0-windows10.0.19041.0` (WinRT projection for media session).

## Feature batch 1–9: DONE

All nine shipped. Widgets: text, tray, clock, sysmon, media, weather, quicklaunch, calendar.

Test widgets in the live `%APPDATA%\MobreadModernDock\config.json`: `sysmontest0001`, `mediatest0001`, `weathertest01` (Denver), `quicktest0001`, `caltest000001`. Remove from Settings › Widgets when done evaluating.

## Loose ends

- **Sponsor button** still doesn't render on the repo page (`showSponsorButton:false`) despite FUNDING.yml on `main`, repo detached, API reporting the Ko-fi link. Last idea: Settings › General › Features › Sponsorships checkbox. README badge works regardless.
- `dotnet/installer/build.ps1` defaults to `1.2.0` — bump to `1.3.0` for the first release, then remove the "no packaged release yet" line from the README.
- Weather widget: the geocoding search in Settings hasn't been exercised by hand (only the fixed-coordinate path was verified live). Worth one manual click-through.
- Legacy `%APPDATA%\CedroModernDock` folder is left in place after migration (by design).

## Feature batch 10–17 (from the original list): NOT STARTED

**Polish**
10. Per-monitor dock — a dock instance on each display.
11. Themes / presets — save and switch named appearance sets (color, opacity, rows, icon size) with one click.
12. Hotkeys — global keys to toggle dock visibility, open Settings, or launch item N.
13. Bounce / attention — animate an icon when its app flashes the taskbar for attention.
14. Import from taskbar — one button in Settings that reads the pinned taskbar shortcuts.
15. Portable mode — config next to the exe instead of `%APPDATA%`.

**Hardening**
16. Installer — wire `dotnet/installer/build.ps1` to produce an MSI for this repo; auto-update check against GitHub releases.
17. Startup delay — wait for explorer before attaching to the desktop (docks can race explorer at login).

## Ideas not started

- Media widget: seek bar / elapsed time (GSMTC exposes `GetTimelineProperties()`).
- Weather: hourly strip; auto-locate via `Windows.Devices.Geolocation`.
- Calendar: Outlook/Google events would need OAuth — out of scope unless asked.
- Quick launch: drag-drop reordering in the widget itself (currently ▲▼ in settings).

## Next session

Load the `mobread-modern-dock-dev` skill first — it has the build workflow, widget-authoring pattern and Win11 pitfalls.
