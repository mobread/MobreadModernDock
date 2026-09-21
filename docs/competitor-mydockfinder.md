# Competitor feature gap — MyDockFinder

_Researched 2026-09-20 from the Steam store page (app 1787090) and mydockfinder.com._

## Quick wins (≤ 1 session each)

| Feature | Notes |
|---|---|
| **Acrylic/Mica blur behind the dock**, adjustable intensity | Their headline feature. Avalonia: `TransparencyLevelHint = AcrylicBlur` / `Mica` on Win11. Our "transparency" is flat alpha only. |
| **Custom icon per dock item** | Right-click → Change icon (.ico/.png/.exe). No per-item override today. |
| **Drag files/folders from Explorer onto the dock to pin** | Batch drop incl. UWP shortcuts. We pin only via Add Program / Add Folder dialogs. |
| **Drop a file onto an app icon to open it with that app** | `ShellExecute(exe, path)`. |
| **Power actions** (shutdown / restart / sign out / sleep / lock) | As a Windows-module item or small widget. |
| **Follow system light/dark theme** | Swap dock colour preset on `HKCU\...\Themes\Personalize\AppsUseLightTheme` change. |
| **Config export/import** | Their equivalent is Steam Cloud backup. One JSON; Settings › General button. |

## Medium (a few sessions)

| Feature | Notes |
|---|---|
| **Launchpad-style app grid** | Overlay grid of installed apps with search. New window + Start-menu enumeration. |
| **Volume widget: per-app mixer + output-device switch** | Core Audio `IAudioSessionManager2`; `IPolicyConfig` for device switch. |
| **Volume OSD** | Popup on volume keys. Pairs with the above. |
| **Folder stacks v2** | Thumbnails, nested navigation, sort order, drag files *out* (copy/move). |
| **Brightness control** | DDC/CI `SetVCPFeature` for external monitors; WMI for laptop panels. |
| **Per-app hidden mode** | Item-level "show only when running" etc. |

## Hard / probably skip

| Feature | Why |
|---|---|
| Window-minimise animations (genie/scale) | Needs D3D11 window capture + DWM cloaking; they ship a separate "Dockmod" process for it. |
| Taskbar progress bar mirrored on icons | `ITaskbarList3` progress is write-only; no public read API. |
| Message-count badges for chat apps | App-specific heuristics per Chinese messenger. Could do a generic "unread" badge from the attention signal we already detect, not real counts. |
| WiFi / Bluetooth managers | Heavy WinRT plumbing for marginal value over the system flyouts. |

## Already at parity

Weather, sysmon, media controls, tray mirror, window previews, multi-monitor, 4K, icon tint/mask, multiple hide modes, quick-launch folder, drag reorder.

## Suggested order

1. Acrylic blur (biggest visual gap, cheapest)
2. Drag-drop pinning + drop-to-open
3. Custom icons
