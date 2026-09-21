# Community feedback gap analysis — r/desktops launch thread

_Source: [r/desktops — "Free and Open-source alternative to Winstep Nexus Dock! Cedro Modern Dock"](https://www.reddit.com/r/desktops/comments/1s6i6r9/free_and_opensource_alternative_to_winstep_nexus/) by u/Arthur_DK7, ~6 months old, 343 upvotes, 52 comments. Analysed 2026-09-20._

**Framing:** this is the *upstream* (Cedro) announcement thread, posted before the
mobread fork. The fork inherits the codebase, so it inherits this feedback and
these users' expectations. Several items OP publicly promised ("added to the
backlog") are now shipped here — worth saying so if the fork is ever announced.

Status legend: **✅ shipped** · **🟡 written this session, uncommitted/untested** · **❌ open**

---

## Already answered since the thread

These were the top asks; the fork closes them. Good release-notes material.

| Request | Who | Status |
|---|---|---|
| "needs blur background" | u/… (1 upvote) | 🟡 acrylic + blur backdrop, per-window, region-clipped to the rounded bar |
| "Dragging icons to the dock to add is a priority" | CindySoLoud | 🟡 drag files/folders from Explorer to pin, at the drop position |
| "drag and drop one or several icons… dropping existing shortcuts… copying all of the shortcuts from Quick Launch\User Pinned\TaskBar" | (2 comments) | ✅ **Import Taskbar Pins** does exactly this use case in one click · 🟡 drag-drop covers the manual path |
| "Why cant I add more than one programs at a time?" | (1) | ✅ `AllowMultiple = true` in the file picker |
| "option to pin the dock always on top, other than full-screen apps like games" | (1) | ✅ `alwaysOnTop` + `hideInFullscreen` — exactly this pair |
| "click on icon in the dock does not minimize windows" | (last comment) | ✅ taskbar semantics: focus / minimize / cycle (`ClickRunning`) |
| "when I add new program to dock, clicking opens a NEW window instead of the last opened one" | (last comment) | ✅ same fix — click activates the existing window |
| "minimize applications to the dock instead of the taskbar" (e.g. unpinned Spotify) | CoolSquid26 (+9 on OP's promise) | ✅ *partially*: unpinned running apps appear in the dock with previews. See gap #2 — true minimize-to-dock is still not it |
| "abrir uma subpasta … aparecendo as icones adicionadas nessa pasta" | (PT) | ✅ folder stacks |
| "mudar a icone com melhor qualidade" / "que permita usar iconos png" | (PT + ES) | 🟡 custom per-item icon (.png/.ico/.exe/.dll) via right-click |
| "ubicar en los bordes de pantalla" | (ES) | ✅ edge snapping + configurable gap |
| "ocultarse automáticamente" / "auto ocultar a barra" | (ES + PT) | ✅ auto-hide (dock and per-widget) |
| "configurando o espaçamento entre as icones" | (PT) | ✅ spacing slider (0–20) |
| "being able to see exact value set for any configured size or spacing" | (1) | ✅ every slider row shows its numeric value (session-5 Settings rebuild) |
| "opciones en el boton derecho" | (ES) | ✅ *partially*: right-click → unpin · 🟡 + change/reset icon |

---

## Open gaps, by weight of evidence

### 1. Code signing / VirusTotal false positives — **distribution blocker**
Raised by **three** separate users; the longest sub-thread in the whole post
("It has fking trojans guys"). OP's answer — open source, compile it yourself,
5/63 engines, Winstep itself trips 3 — is correct but does not survive contact
with a casual downloader. One user only relented after OP argued it.

This is the single highest-leverage non-feature item, and it lands directly on
the **v1.3.0 release** in `HANDOFF.md`: shipping an unsigned MSI reproduces this
thread verbatim. Options, cheapest first:
- publish SHA-256 checksums + a VirusTotal permalink in the release notes and README, pre-empting the accusation;
- submit false-positive reports to the worst offenders (Bkav, Gridinsoft, VBA32, Zillya);
- sign properly — an Azure Trusted Signing account is ~$10/mo and is the real fix;
- a reproducible-build recipe so a third party can verify the binary matches the tag.

### 2. Minimize-to-dock (true window capture)
OP promised this ("I'll add this feature to the backlog and try to provide it on
the next update") and it drew **+9** — the most-upvoted reply in the thread. One
user: *"This is the main reason I still use the windows bar."*

What we have is adjacent but not the ask: unpinned running apps are *listed*.
The request is that minimizing a window sends it **into the dock** instead of the
taskbar. Needs a shell hook on minimize (`HSHELL_WINDOWACTIVATED` /
`WM_SYSCOMMAND` interception) plus DWM thumbnail iconisation — genuinely hard,
and the taskbar must be hidden for it to make sense. Ties into gap #4.

### 3. Themes / skins — recurring, and someone offered money
Three independent asks: "can you edit or theme the dock?", *"I'd be interested,
maybe even pay for if there was an ability to use skins or themes like ObjectDock
and WorkShelf (Nexus)"*, and a general "more customisation". Colour + roundness +
opacity + presets exist; a **skin format** (bar background image, 9-slice
borders, per-state icon overlays, shareable folder or file) does not. The
appearance-preset JSON is the natural foundation — extend it rather than invent
a parallel system.

### 4. "Use it as a taskbar instead of a desktop widget"
Wants real `SHAppBarMessage`/`ABM_SETPOS` appbar registration so maximised
windows reserve space around the dock. Today `hideTaskbar` hides the shell's bar
but nothing reserves the screen edge, so maximised windows sit underneath.
Prerequisite for #2 feeling correct.

### 5. Separators as dock items
*"I'd like to see a possibility to add separators"*. Currently one hardcoded
separator divides pinned from running apps. Cheap: a `DockSeparatorItemModel`
(new `@type`, fixed narrow width, no click) that users can insert and reorder —
the drag-reorder and `KeepSettingsLast` machinery already handles arbitrary items.
**Best effort-to-value ratio on this list.**

### 6. Launchpad / sub-docks
*"Can't forget about Launch Pad!"* and *"I love the Nexus sub dock feature"* — a
nested dock that expands from an icon. Overlaps the **Launchpad-style app grid**
already logged as *Medium* in `competitor-mydockfinder.md`; treat as one feature.

### 7. Default icons on first run
*"estaría bueno que al arrancar por primera vez ya contara con algunos iconos
predeterminados"*. `LoadDefaultItems()` adds only the Settings gear, so a fresh
install looks broken-empty. Fix: seed from Import-Taskbar-Pins on first run, or
ship a few safe defaults (Explorer, default browser, This PC). Trivial, and it
fixes the first-impression path — which is what this thread *was*.

### 8. Settings window unusable below 1080p
*"que la ventana de configuración se pueda visualizar en forma correcta en
resoluciones inferiores a 1920x1080, ej 1280x720, sino no se puede aplicar o
nada"* — i.e. the buttons are unreachable, so nothing can be applied.

`SettingsWindow.axaml` is `Height="700" MinHeight="520"`. At 720p, 700px plus the
title bar exceeds the work area, and the window opens taller than the screen.
The session-5 rebuild put everything in a `ScrollViewer`, which probably helps,
but **this is untested at 720p and was reported as a hard blocker.** Verify at
1280×720 and clamp the startup height to the work area.

### 9. Discoverability: how to close / uninstall
One user posted a mini-rant ("Did i mention that you can uninstall it????",
*"Be warned there is no way to close or remove this app once installed. I am
still trying to find a way two weeks later"*). OP's reply — tray → right-click →
Exit — resolved it, so the feature exists and the **affordance** doesn't. Cheap
wins: a first-run hint pointing at the tray icon, an Exit entry in the dock's own
right-click menu, and a README "How to exit / uninstall" section.

### 10. Browser-based app (PWA) shortcuts
*"Cant add shortcuts for browser based apps."* OP couldn't reproduce. Chrome/Edge
PWA `.lnk`s point at `chrome.exe --app-id=…`; `ShellLinkResolver` keeps arguments,
so this may already work — but the reporter also had other problems. Needs a real
repro before any code changes.

### 11. Linux support
Asked twice (ES + FR). OP: Windows-only. Architecturally the fork is *ready* —
`Core` is clean and `Infrastructure.Windows` is the only platform assembly — but
tray/appbar/UIA/DWM have no Linux analogue. Out of scope; worth one honest line
in the README instead of silence.

### 12. Misc
- *"decrease dock height"* — icon size goes to 16px, but bar `Padding="10"` is fixed. Make padding a setting.
- *"add dock items by hovering icons over it"* — spring-loaded hover; 🟡 drag-drop delivers the substance.
- *"animaciones"* — hover scale + attention bounce exist; launch/open animations don't.
- *"buscar colaboradores"* — CONTRIBUTING.md + good-first-issue labels.
- *"como eu adiciono meus jogos nessa dock"* — a user couldn't figure out how to add games → same discoverability theme as #7/#9.

---

## Suggested order

1. **Release hygiene for v1.3.0** — checksums + VirusTotal link + FP reports (#1). Blocks the release already queued in `HANDOFF.md`.
2. **Separators** (#5) — smallest real feature here, explicitly requested.
3. **Default items on first run** (#7) + **exit/uninstall discoverability** (#9) — hours of work, fixes the first-impression path.
4. **Verify Settings at 1280×720** (#8) — reported as a hard blocker; may already be fixed, must be checked.
5. **Appbar reservation** (#4) → unlocks **minimize-to-dock** (#2), the +9 ask.
6. **Skins/themes** (#3) — the only feature anyone offered to pay for.

Cross-reference `competitor-mydockfinder.md`: its quick-win list (blur,
drag-drop pinning, custom icons) is *the same set* this thread's users asked for,
which is a strong signal those three were the right call. Launchpad appears on
both lists (#6 here, Medium there).
