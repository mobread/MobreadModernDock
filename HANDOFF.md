# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 4, end)._

## State

- Repo: `github.com/mobread/MobreadModernDock`. Local: `C:\Users\micha\claude-projects\MobreadModernDock`.
- `main` = `feature/mobread-customizations` (kept in sync by no-ff merges; `main` is default and renders the README). Head `3dc820f`.
- 79/79 tests. TFM `net9.0-windows10.0.19041.0`. Version `1.3.0` in `MobreadModernDock.csproj`.
- WiX **6.0.2** installed globally (v7 needs the paid OSMF EULA). `dotnet\installer\build.ps1` produces the MSI; artifacts gitignored.
- Load the `mobread-modern-dock-dev` skill first — build workflow, widget pattern, Win11/Avalonia pitfalls.

## Everything shipped

Widgets: text, tray (rows/columns), clock, sysmon, media, weather, quicklaunch, calendar.
Dock: multi-row, auto-hide, fullscreen hide, edge snapping (configurable gap), always-on-top, drag reorder, taskbar clicks, folder stacks, `.lnk`, previews, per-monitor mirror, presets, attention bounce, Import Taskbar Pins, portable mode, update check, shell-readiness wait, tooltips on the outward side.
Per widget: opacity, layer (follow/top/desktop), auto-hide (only while snapped to an edge).

Skipped by request: #12 global hotkeys.

## Live config notes

`%APPDATA%\MobreadModernDock\config.json` has `mirrorOnAllMonitors: true` and five test widgets (`sysmontest0001`, `mediatest0001`, `weathertest01`, `quicktest0001`, `caltest000001`). Tray is 2 columns. Remove/adjust in Settings.

## Not exercised by hand (code paths verified only)

Weather city search · presets Save/Apply/Delete UI · Import Taskbar Pins button · portable mode with a real `portable.marker` · update check (no release exists yet, reports "Couldn't reach GitHub") · per-widget layer/auto-hide via the Settings UI (tested via config edits).

## Loose ends

- **First release:** rebuild MSI (`build.ps1`), `gh release create v1.3.0 dotnet/installer/MobreadModernDock-1.3.0-x64.msi`, drop the "no packaged release yet" README line. Update check reads the `v1.3.0` tag.
- **Sponsor button** still `showSponsorButton:false` on the repo page. Last idea: Settings › General › Features › Sponsorships checkbox.
- Legacy `%APPDATA%\CedroModernDock` left in place after migration (by design).
- Mirror docks: not tested with a vertical dock or auto-hide on the secondary.

## Ideas not started

- #12 Hotkeys (`RegisterHotKey` + message window; `AttentionMonitor` is the template).
- Media: seek bar / elapsed (`GetTimelineProperties()`). Weather: hourly strip, auto-locate. Quick launch: in-widget drag reorder.
- Tooltip placement for widgets (only the dock does it now).

## Pitfalls learned (also in the skill)

- Avalonia `ItemsPanel` templates have no DataContext — bind with `Source = vm`.
- Win11 24H2 never sends `HSHELL_FLASH`; `FlashWindowEx` arrives as bare `HSHELL_REDRAW` per blink. Detect two in 1.5 s.
- Style-class animations were unreliable; VM-driven `DispatcherTimer` + bound offset works (`BounceAnimator`).
- Drawing outside the dock bar needs `ClipToBounds=False` on Border, both ItemsControls, and the Button's `PART_ContentPresenter`, plus window headroom.
- Tooltips live in the dock's overlay layer — invisible to UIA/EnumWindows; verify by eye.
- Shell hooks don't reach `HWND_MESSAGE` windows; use a hidden plain top-level.
