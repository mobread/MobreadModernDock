# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 7)._

## Start here (next session)

1. **Decide on code signing, then cut v1.3.0.** Verified this session: `MobreadModernDock.exe` has an empty PE cert table and the 51.9 MB MSI is unsigned — both will trip SmartScreen and some AV engines. This is a *decision*, not a bug: sign properly (Azure Trusted Signing, ~$10/mo) or ship unsigned with SHA-256 checksums + a VirusTotal permalink in the release notes and README. Then rebuild the MSI (`dotnet\installer\build.ps1`), `gh release create v1.3.0 dotnet/installer/MobreadModernDock-1.3.0-x64.msi`, drop the "no packaged release yet" README line, and confirm the in-app update check picks up the tag.
2. **Set the hover delay to taste.** `previewDelayMs` is absent from the live config, so it is on the 400 ms default — *shorter* than the 1 s that felt right during testing. Settings › Behavior › Apps › Hover delay.
3. **Eyeball the session-6/7 additions** — blur radios, follow-theme checkbox, Export/Import, the colour wheels, separators, magnification. All verified by measurement, not by taste.
4. **Click through the paths only verified via config edits** — per-widget layer/auto-hide, presets Save/Apply/Delete, Import Taskbar Pins, weather city search.
5. **Next features:** `docs/community-feedback-reddit.md` (community-ranked) and `docs/competitor-mydockfinder.md` (competitor-ranked). Cheapest remaining: default items on first run, exit/uninstall discoverability. Biggest: appbar reservation → minimize-to-dock (+9 on the launch thread).

## Session 7 changes

- **Colour wheel** replaces the 12-swatch grids for both dock background and icon tint (`ColorSpectrumShape="Ring"`). The old presets survive as the picker's palette tab. Removed two modal "Custom…" dialogs. Also fixed a **pre-existing** bug this exposed: `DockColorBrush`/`TintColorBrush` were only re-notified from the `_isInitialized`-gated change router, so the preview swatch kept the constructor default until something else changed. `5734772`.
- **Separator layout fix** — a 2px divider was reserving a full icon-width cell. Replaced `UniformGrid` with `Views/DockItemsPanel`, which measures each child individually while keeping wrapping, vertical-dock orientation and line centring. `GridRows`/`GridColumns`/`NotifyGridShape` deleted with it. `80b6ff4`.
- **macOS-style hover magnification**, single-row only. Geometry is pure functions in `Core/Application/DockMagnification` (10 tests). Off by default; Settings › Appearance › Icons. `0815863`.
- **Hover delay before window previews/tooltips** — first scoped to magnification (`d833d73`), then made a 0–2000 ms setting in Settings › Behavior › Apps (`00c7458`, default 400 ms, 0 = instant). Folded the two duplicate pointer-entered handlers into one `SchedulePreview` path.

## State

- Repo: `github.com/mobread/MobreadModernDock`. Local: `C:\Users\micha\claude-projects\MobreadModernDock`.
- `main` = `feature/mobread-customizations` (kept in sync by no-ff merges; `main` is default and renders the README). Both pushed, working tree clean.
- **103/103 tests.** TFM `net9.0-windows10.0.19041.0`. Version `1.3.0` in `MobreadModernDock.csproj`.
- WiX **6.0.2** installed globally (v7 needs the paid OSMF EULA). `dotnet\installer\build.ps1` produces the MSI; artifacts gitignored.
- Load the `mobread-modern-dock-dev` skill first — build workflow, widget pattern, Win11/Avalonia pitfalls.

## Corrections to the session-6 handoff

- **"Settings unusable at 1280×720" was NOT a bug in this fork.** It came from the upstream Cedro build, which was `520x680` with `CanResize="False"` — genuinely unusable at 720p. This fork already has `CanResize="True"` plus a `ScrollViewer`. Verified by forcing the window to 1280×672: scrollbar present, all six tabs selectable, every button reachable. *Minor*: it clamps at ~697 px tall despite `MinHeight="520"`, so on a 720p screen (~672 px work area) it is ~25 px taller than the work area — usable, not perfect.
- **Code signing IS a real issue for this fork** — confirmed against the actual artifacts, not inherited from upstream complaints.
- Lesson: complaints in `docs/community-feedback-reddit.md` describe the *upstream* program. Verify against this codebase before acting.

## Not verified by taste (sessions 6–7)

Blur/acrylic (pixel diff only), acrylic at various dock colours, the colour wheel's size/placement, and the magnification influence radius (hardcoded 2.5 icon widths in `DockMagnification.InfluenceIcons`). Config import was exercised through its code path, not with a real exported file. Power actions were **not** executed — running them would shut the machine down; only launcher wiring and picker entries were checked. The hover delay is confirmed end-to-end at 0 ms (46 ms observed) and at the service layer for all values; synthetic-hover timing at longer delays was unreliable and proved nothing.

## Everything shipped

Widgets: text, tray (rows/columns), clock, sysmon, media, weather, quicklaunch, calendar.
Dock: multi-row, auto-hide, fullscreen hide, edge snapping (configurable gap), always-on-top, drag reorder, taskbar clicks, folder stacks, `.lnk`, previews, per-monitor mirror, presets, attention bounce, Import Taskbar Pins, portable mode, update check, shell-readiness wait, tooltips on the outward side, acrylic/blur backdrop, custom per-item icons, Explorer drag-drop (pin + open-with), power actions, follow system theme, config export/import, user separators, colour wheel pickers, hover magnification, configurable hover delay.
Per widget: opacity, layer (follow/top/desktop), auto-hide (only while snapped to an edge).

Skipped by request: #12 global hotkeys.

## Live config notes

`%APPDATA%\MobreadModernDock\config.json`: `mirrorOnAllMonitors: true`, `magnifyIcons: true` at `1.8`, three user separators (browsers | tools | comms), 8 widgets of which only the tray widget is enabled. `previewDelayMs` unset → 400 ms default.

## Not exercised by hand (code paths verified only)

Weather city search · presets Save/Apply/Delete UI · Import Taskbar Pins button · portable mode with a real `portable.marker` · update check (no release exists yet, reports "Couldn't reach GitHub") · per-widget layer/auto-hide via the Settings UI.

## Loose ends

- **First release:** see item 1 above.
- **Sponsor button** still `showSponsorButton:false` on the repo page. Last idea: Settings › General › Features › Sponsorships checkbox.
- Legacy `%APPDATA%\CedroModernDock` left in place after migration (by design).
- Mirror docks: not tested with a vertical dock or auto-hide on the secondary. Multi-monitor mirroring is proven for 3+ screens by `MultiMonitorMirrorTest` (fake provider, 4 monitors incl. negative coordinates) but only ever run on 2 real ones. **No `WM_DISPLAYCHANGE` handler** — plugging in a monitor needs a settings toggle or restart to get a dock on it.
- Magnification influence radius is a hardcoded constant, not a setting.

## Ideas not started

- #12 Hotkeys (`RegisterHotKey` + message window; `AttentionMonitor` is the template).
- Media: seek bar / elapsed (`GetTimelineProperties()`). Weather: hourly strip, auto-locate. Quick launch: in-widget drag reorder.
- Tooltip placement for widgets (only the dock does it now).

## Pitfalls learned (also in the skill)

- **An unconditional style setter beats a local value.** The item template's `Style Selector="Button"` RenderTransform setter silently cancelled the whole magnification effect. Hover-zoom styles now live at window level behind `ItemsControl:not(.magnified)`. This is the second time this trap has cost real time — the file even had a comment warning about it.
- Avalonia `ItemsPanel` templates have no DataContext — bind with `Source = vm`, or push values in from code-behind (`SyncPinnedPanel`). Note the panel is realized *after* `Initialize()`, so anything pushed in early can be stale: `UpdateMagnifier` re-reads the setting each move for exactly this reason.
- `UniformGrid` sizes every cell to the widest child — fatal for narrow items like separators.
- Win11 24H2 never sends `HSHELL_FLASH`; `FlashWindowEx` arrives as bare `HSHELL_REDRAW` per blink. Detect two in 1.5 s.
- Style-class animations were unreliable; VM-driven `DispatcherTimer` + bound offset works (`BounceAnimator`).
- Drawing outside the dock bar needs `ClipToBounds=False` on Border, both ItemsControls, and the Button's `PART_ContentPresenter`, plus window headroom.
- Tooltips live in the dock's overlay layer — invisible to UIA/EnumWindows; verify by eye.
- Shell hooks don't reach `HWND_MESSAGE` windows; use a hidden plain top-level.

## Verification tooling (scratch dir)

`%LOCALAPPDATA%\hermes\cache\scratch\`:

- `dock_buttons.ps1` — **read-only** dump of dock buttons with indices/rects. Use this before invoking anything.
- `open_settings.ps1` — opens Settings by UIA-invoking the last pinned button; `dump_tabs.ps1 -Hwnd <h>` lists every control per tab.
- `hover_capture.py <screenX> <out> [cx cy cw ch] [y]` — **SendInput** hover + screenshot in one process. `SetCursorPos` alone does not reliably deliver `WM_MOUSEMOVE` to the dock; `SendInput` needs `MOVE|ABSOLUTE|VIRTUALDESK` (0x4000 matters on multi-monitor).
- `find_running_icons.py` — locates pinned icons whose app is running, by the indicator dot. **A window preview only appears for running apps** — testing against a closed one produces a false negative (cost me a wrong conclusion this session).
- `measure_delay.py`, `winstate.py`, `shot.ps1`, `blurprobe.py`, `power_icons.py`, `i18n_*.py`.
- **Never brute-force UIA-invoke dock buttons to find one** — it launches whatever it touches. That opened cmd.exe, Realtek Audio Console and Windows Settings on the user's desktop this session.
