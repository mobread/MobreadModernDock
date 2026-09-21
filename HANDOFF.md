# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 6)._

## Start here (next session)

1. **Release hygiene before cutting v1.3.0.** Unsigned-binary VirusTotal false positives are the single most damaging issue in the community feedback (three separate users, longest sub-thread on the launch post). At minimum publish SHA-256 checksums + a VirusTotal permalink in the release notes and README, and file false-positive reports with Bkav/Gridinsoft/VBA32/Zillya. See `docs/community-feedback-reddit.md` #1.
2. **Cut v1.3.0.** Rebuild the MSI (`dotnet\installer\build.ps1`), then `gh release create v1.3.0 dotnet/installer/MobreadModernDock-1.3.0-x64.msi`, drop the "no packaged release yet" README line, and confirm the in-app update check picks up the tag.
3. **Eyeball the session-6 additions** — the blur radios and follow-theme checkbox in Appearance, Export/Import in General, and a separator on the dock. Verified by UIA + pixel diff, not by eye; acrylic vs blur is a taste call.
4. **Verify Settings at 1280×720** — reported on the launch thread as a hard blocker ("no se puede aplicar o nada"). The window is `Height=700`; the session-5 ScrollViewer may have fixed it, but it is untested at that size. `docs/community-feedback-reddit.md` #8.
5. **Click through the paths only verified via config edits** — per-widget layer/auto-hide, presets Save/Apply/Delete, Import Taskbar Pins, weather city search.
6. **Next features:** `docs/community-feedback-reddit.md` (community-ranked) and `docs/competitor-mydockfinder.md` (competitor-ranked). Cheapest remaining: default items on first run, exit/uninstall discoverability. Biggest: appbar reservation → minimize-to-dock (+9 on the thread).

## Session 6 changes

Six of the seven MyDockFinder quick wins, plus the top community ask:

- **Acrylic/blur backdrop** (`blurMode`: none/blur/acrylic) for dock and widgets, via `SetWindowCompositionAttribute` + a rounded GDI region so the blur follows the bar instead of filling the window rect. `bf87eb0`.
- **Per-item custom icons** (.png/.ico/.exe/.dll) — right-click → Change icon / Reset icon. `adc84f4`.
- **Drag-drop from Explorer**: drop on the bar to pin, drop on a program icon to open with it. `be38ff9`.
- **Power actions**: shut down / restart / sign out / sleep / lock as Windows modules, with confirmation on the destructive three and generated icons. `7f06638`.
- **Follow Windows light/dark theme**. `93a0b5a`.
- **Config export/import** (Settings › General). `1eda66b`.
- **User-placed separators** — the launch thread's cheapest concrete request. `7c7e24e`.
- **Fix:** the running-app dot no longer pushes the icon up (was a ~4px hop when an app opened). `a72b7e3`.
- **Docs:** `docs/community-feedback-reddit.md` — all 52 launch-thread comments analysed against current state.
- Scratch: `dock_buttons.ps1` (read-only dock button dump), `open_settings.ps1`, `dump_tabs.ps1 -Hwnd <h>`, `blurprobe.py`, `power_icons.py`, `i18n_session6.py`.

## State

- Repo: `github.com/mobread/MobreadModernDock`. Local: `C:\Users\micha\claude-projects\MobreadModernDock`.
- `main` = `feature/mobread-customizations` (kept in sync by no-ff merges; `main` is default and renders the README).
- 86/86 tests. TFM `net9.0-windows10.0.19041.0`. Version `1.3.0` in `MobreadModernDock.csproj`.
- WiX **6.0.2** installed globally (v7 needs the paid OSMF EULA). `dotnet\installer\build.ps1` produces the MSI; artifacts gitignored.
- Load the `mobread-modern-dock-dev` skill first — build workflow, widget pattern, Win11/Avalonia pitfalls.

## Not verified by eye (session 6)

Blur/acrylic was confirmed by pixel diff (69% of dock pixels change, background rgb(13,13,19)→rgb(30,30,34)) and separators by screenshot, but nobody has judged whether acrylic *looks* right at various dock colours. Config import was exercised only through its code path, not with a real exported file. Power actions were **not** executed — running them would shut the machine down; only the launcher wiring and the picker entries were checked.

## Everything shipped

Widgets: text, tray (rows/columns), clock, sysmon, media, weather, quicklaunch, calendar.
Dock: multi-row, auto-hide, fullscreen hide, edge snapping (configurable gap), always-on-top, drag reorder, taskbar clicks, folder stacks, `.lnk`, previews, per-monitor mirror, presets, attention bounce, Import Taskbar Pins, portable mode, update check, shell-readiness wait, tooltips on the outward side, acrylic/blur backdrop, custom per-item icons, Explorer drag-drop (pin + open-with), power actions, follow system theme, config export/import, user separators.
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
