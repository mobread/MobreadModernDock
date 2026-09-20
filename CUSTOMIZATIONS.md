# Mobread Modern Dock — what's different from upstream

What this fork adds on top of [Cedro-Software/cedro-modern-dock](https://github.com/Cedro-Software/cedro-modern-dock).

**18 feature commits · ~6,100 lines across 84 files · 78 tests passing**

All work lives on `feature/mobread-customizations`. `main` tracks upstream verbatim (plus `.github/FUNDING.yml`).

---

## Dock behaviour

| Feature | What it does |
|---|---|
| **Multi-row dock** | `Rows` slider (1–4) in Icons Customization. Icons wrap into N rows horizontally, N columns vertically. Default 1 keeps existing configs visually unchanged. |
| **Auto-hide** | Dock slides to the nearest screen edge (ease-out, 160 ms) leaving a 3 px sliver after a 600 ms grace period; returns when the pointer touches the sliver. Suppressed during hover previews and drag-reorders. |
| **Fullscreen auto-hide** | Dock and widgets hide while a fullscreen app is active. `FullscreenDetector` combines `SHQueryUserNotificationState` with a foreground-covers-monitor check, so borderless games count but maximized windows don't. |
| **Edge snapping** | Drag ending within 24 px of a screen edge or centre line pulls the window flush (8 px margin; exact on centre), each axis independently. Applied on the position-persist timer so it never fights the OS drag loop. |
| **Always on top** | Toggles dock + widgets between the desktop layer (behind windows, survives Win+D) and `HWND_TOPMOST`, preserving screen position. |
| **Drag to reorder** | Drag a pinned icon past a 6 px threshold; a drop indicator marks the target gap. Plain clicks still launch. Drop target resolves by nearest cell in 2D, so it works across rows. |
| **Settings gear stays last** | `DockModel.KeepSettingsLast()` runs after Add/Move/Swap and once on load, normalizing old configs. The gear isn't draggable and drop targets clamp to the slot before it. |

## Launching & window management

- **Taskbar-style clicks** — clicking an already-running pinned program focuses its window instead of launching a second instance; minimizes it if already foreground; cycles through windows on repeat clicks. Mouse wheel over the icon steps through them.
  - `ActivateWindow` briefly attaches to the foreground thread's input queue — `SetForegroundWindow` is refused for a `WS_EX_NOACTIVATE` window like the dock, which is why focus-from-dock never worked reliably.
- **Right-click pin/unpin** — "Pin to Dock" on running-but-unpinned apps, "Unpin from Dock" on pinned ones. Localized across all 21 bundles.
- **Folder stacks** — clicking a folder item opens a macOS-style icon-grid popup anchored to the dock icon instead of launching Explorer. Folders drill down in place, right-click reveals in Explorer, subfolders first then files newest-first, 60 max. Icons come from the shell image list.
- **`.lnk` shortcut support + arguments** — "Add Program" accepts shortcuts (multi-select). `ShellLinkResolver` (`IShellLinkW` COM) reads target/args/workdir/icon and repairs the two non-exe target shapes seen on real taskbar pins: MSI advertised shortcuts whose target is the `.ico` (WSL), and shell-object shortcuts with an `.exe` icon but no target (File Explorer). Working directory now defaults to the executable's folder.

## Widget framework

A generic `IWidgetProvider` + `WidgetWindow` system — each provider supplies a content view and settings panel; the framework supplies chrome (dock color/rounding), drag, position persistence, desktop/topmost layering, opacity and fullscreen auto-hide. Settings › Widgets is a list: add by type, remove, show/hide, per-widget options.

| Widget | Notes |
|---|---|
| **Text** | User-defined text with `{host}` and `{user}` templates. |
| **System tray** | Reads the Win11 notification area via **UI Automation** — the XAML island is invisible to Win32 enumeration. Icon bitmaps come from the shell's own cache (`HKCU\Control Panel\NotifyIconSettings\*\IconSnapshot`) because DWM excludes the taskbar from screen capture. Left-click = UIA Invoke; right-click = synthesized click at the icon's physical position, read under per-monitor DPI awareness. |
| **Clock** | 10 layout presets (12/24 h, seconds, weekday, short/long/ISO date) plus free .NET format strings, listed with live samples. Optional second date line. Ticks align to the wall clock — per-second only when the format shows seconds. Invalid patterns fall back to `HH:mm`. |
| **System monitor** | CPU / RAM / GPU / network as colour-coded bars (green → amber → red) via Windows performance counters. Per-metric toggles, compact (bars-only) mode, 0.5–5 s refresh. CPU uses `% Processor Utility` to match Task Manager; GPU sums `engtype_3D` engine instances (counters persist between samples — rate counters read 0 on first call); network bar scales against a decaying peak. |

## Windows integration

- **Hide Windows taskbar** — hides `Shell_TrayWnd` and all `Shell_SecondaryTrayWnd` and flips the appbar to auto-hide so the work area expands. Restored on uncheck, shutdown, `ProcessExit` and unhandled exception; a taskbar left hidden by a force-kill is repaired at next launch.
  - The shell re-creates `Shell_SecondaryTrayWnd` with a new HWND after appbar changes and display events, so a 750 ms enforcer re-hides any taskbar that reappears.
  - Tray right-click with the taskbar hidden shows it for ~100 ms rather than the menu's whole lifetime — re-hide fires as soon as the app's menu window appears. Menu detection is "a new popup-sized top-level window since the pre-click snapshot", which catches Win32 `#32768`, Electron, SDL and XAML menus alike.
- **Multi-monitor positioning** — the dock is reparented to Progman to survive Win+D, but Progman spans the whole virtual desktop, so Avalonia's parent-relative `Position` landed on the wrong display when the primary monitor isn't at the virtual origin. `ScreenGeometry` provides Win32-backed rects for controls, windows and work areas; `MoveToScreen`/`GetScreenPosition` convert through the desktop parent. Folder stacks and hover previews both use it.

## Appearance

- **Global opacity slider** (20–100%) fades the whole dock — icons and background — plus every widget that follows it. Distinct from the existing Dock Transparency, which only affects the background fill.
- **Per-widget opacity override** — "Follow global" (default) or "Custom". Stored in the widget's settings dict, so no per-type work is needed.
- **Resizable settings window** — 780×544 fixed → 860×700 default, resizable, min 720×520.

## Other

- **Single shutdown path** — `App.RequestShutdown()` is the one exit route, so the taskbar is always restored.
- **i18n** — new keys added across all 21 language bundles.
- **Tests** — 78 passing, including `EdgeSnapperTest`, `ClockFormatsTest`, `ShellLinkResolverTest`, `WidgetOpacityTest`, `WidgetServiceTest`.

---

## Building

```bash
cd dotnet
dotnet build src/MobreadModernDock -c Release
dotnet test tests/MobreadModernDock.Tests -c Release
```

## Support

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Buy%20me%20a%20coffee-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/mobreadmeo)
