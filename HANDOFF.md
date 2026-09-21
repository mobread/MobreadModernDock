# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 4)._

## State

- Repo: `github.com/mobread/MobreadModernDock` (detached from the Cedro fork). Local: `C:\Users\micha\claude-projects\MobreadModernDock`.
- `main` and `feature/mobread-customizations` are kept in sync by no-ff merges; `main` is the default branch and renders the README.
- 78/78 tests. TFM `net9.0-windows10.0.19041.0`. Version `1.3.0` in `MobreadModernDock.csproj` (single source of truth).
- WiX **6.0.2** installed globally (v7 needs the paid OSMF EULA). `dotnet/installer/build.ps1` produces `MobreadModernDock-1.3.0-x64.msi` (49 MB); artifacts are gitignored.

## Feature batches 1–17: DONE (except #12 hotkeys — skipped at user's request)

Widgets: text, tray, clock, sysmon, media, weather, quicklaunch, calendar.
Polish: per-monitor mirror (#10), presets (#11), attention bounce (#13), taskbar import (#14), portable mode (#15), installer + update check (#16), shell-readiness wait (#17).

Live config has test widgets (`sysmontest0001`, `mediatest0001`, `weathertest01`, `quicktest0001`, `caltest000001`) and **`mirrorOnAllMonitors: true`** — turn off in Settings › General if not wanted.

## Loose ends

- **Sponsor button** still doesn't render (`showSponsorButton:false`). Last idea: Settings › General › Features › Sponsorships checkbox. README badge works regardless.
- **First release:** `gh release create v1.3.0 dotnet/installer/MobreadModernDock-1.3.0-x64.msi` (rebuild the MSI first — the one on disk predates the final polish commit), then remove the "no packaged release yet" line from the README. The in-app update check reads the `v1.3.0` tag.
- Not exercised by hand: weather city search; presets Save/Apply/Delete UI; Import Taskbar Pins button; portable mode (only the code path, not a real portable folder); update check (no release exists yet, so it reports "Couldn't reach GitHub").
- Legacy `%APPDATA%\CedroModernDock` folder is left in place after migration (by design).

## Pitfalls learned this session (also in the skill)

- Avalonia `ItemsPanel` templates have no DataContext — bind with `Source = vm` or the panel silently ignores it.
- Win11 24H2 never delivers `HSHELL_FLASH` (0x8006); `FlashWindowEx` shows up as bare `HSHELL_REDRAW` (6) per blink. Detect two REDRAWs from one app within 1.5 s.
- Style-class animations (`Classes.attention` + `Style.Animations`) didn't run on `TranslateTransform.Y`; a VM-driven `DispatcherTimer` writing a bound offset is reliable.
- Anything translated outside the dock bar is clipped by *four* layers: Border `ClipToBounds`, both `ItemsControl`s, and the Fluent Button's `PART_ContentPresenter`. All are now off, plus 12 px window headroom.

## Ideas not started

- #12 Hotkeys (global `RegisterHotKey` needs a message window; the `AttentionMonitor` pattern is a template).
- Media widget: seek bar / elapsed time (`GetTimelineProperties()`).
- Weather: hourly strip; auto-locate via `Windows.Devices.Geolocation`.
- Quick launch: drag-drop reordering in the widget itself.
- Mirrors don't get their own auto-hide reveal zone tuned per screen edge — they inherit the primary's setting and it works, but hasn't been tested with a vertical dock.

## Next session

Load the `mobread-modern-dock-dev` skill first.
