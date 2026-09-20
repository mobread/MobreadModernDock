# Mobread Modern Dock

![GitHub Release](https://img.shields.io/github/v/release/mobread/MobreadModernDock?style=for-the-badge)
![GitHub Repo stars](https://img.shields.io/github/stars/mobread/MobreadModernDock?style=for-the-badge)
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/mobread/MobreadModernDock?style=for-the-badge)
![GitHub License](https://img.shields.io/github/license/mobread/MobreadModernDock?style=for-the-badge)
![.NET](https://img.shields.io/badge/.NET-9-5122d3?style=for-the-badge&logo=dotnet&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia-11.3-0080ff?style=for-the-badge&logo=avalonia&logoColor=white)

A macOS-style dock for Windows 11 that can **replace the taskbar** — pinned apps, running apps, folder stacks, live window previews, and a widget system (clock, system monitor, tray icons, text) that all live on the desktop layer or float on top.

Built on [Cedro Modern Dock](https://github.com/Cedro-Software/cedro-modern-dock) by [@arthurdeka](https://github.com/arthurdeka). This project keeps that foundation and adds a lot on top — see [`CUSTOMIZATIONS.md`](CUSTOMIZATIONS.md) for the full list.

<img width="1006" height="382" alt="Mobread Modern Dock screenshot" src="https://github.com/user-attachments/assets/4ed597cc-a8c4-4d0d-ae3e-cc6fb5514b50" />

<br>

> ## How To Install
> 1. Go to [Releases](https://github.com/mobread/MobreadModernDock/releases)
> 2. Download the latest `MobreadModernDock-<version>-x64.msi`
> 3. Run it
>
> Upgrading from **Cedro Modern Dock**? Your shortcuts and settings are picked up automatically on first launch.

<br>

## Features

### Dock
- **Multi-row layout** — 1 to 4 rows (or columns when vertical).
- **Auto-hide** — slides off the nearest screen edge and returns on hover. Also hides while a fullscreen app is running (games count; maximized windows don't).
- **Edge snapping** — drag near a screen edge or the centre line and the dock snaps flush.
- **Always on top or desktop layer** — stays visible over windows, or sits behind them and survives Win+D.
- **Drag to reorder** pinned icons directly on the dock.

### Launching & windows
- **Taskbar-style clicks** — click a running app to focus it, click again to minimize, keep clicking to cycle its windows. Scroll wheel cycles too.
- **Live window previews** on hover, click one to bring it forward.
- **Running apps you haven't pinned** show up on the dock; right-click to pin them.
- **Folder stacks** — folders open as a macOS-style icon grid instead of launching Explorer.
- **`.lnk` shortcut support** with arguments and working directory.

### Widgets
Free-floating panels that follow the dock's colour and rounding, with per-widget opacity and position.

| Widget | |
|---|---|
| **Clock** | 10 presets plus custom .NET format strings, optional second date line. |
| **System monitor** | CPU / RAM / GPU / network bars, colour-coded by load, with per-metric toggles and a compact mode. |
| **System tray** | Your Win11 notification-area icons, left- and right-clickable, so you can hide the taskbar entirely. |
| **Text** | Any text, with `{host}` and `{user}` templates. |

### Windows integration
- **Hide the Windows taskbar** — on every monitor, with the work area expanded. Restored automatically on exit, crash, or next launch.
- **Multi-monitor aware** — the dock and widgets remember which screen they're on, even when the primary monitor isn't at the top-left.

### Appearance
- Global opacity slider, iOS-style icon tint with 12 presets or a custom colour, dock transparency and rounding.

<br>

## Supported Languages

🇺🇸 English · 🇧🇷 Portuguese (Brazil) · 🇪🇸 Spanish · 🇫🇷 French · 🇩🇪 German · 🇯🇵 Japanese · 🇨🇳 Chinese (Simplified) · 🇹🇼 Chinese (Traditional) · 🇮🇳 Hindi · 🇸🇦 Arabic · 🇧🇩 Bengali · 🇷🇺 Russian · 🇵🇰 Urdu · 🇮🇩 Indonesian · 🇳🇬 Nigerian Pidgin · 🇮🇳 Marathi · 🇮🇳 Telugu · 🇹🇷 Turkish · 🇮🇳 Tamil · 🇭🇰 Cantonese · 🇻🇳 Vietnamese

<br>

<!-- SUPPORT -->
## Support This Project

If this dock earned a spot on your desktop, you can leave a tip — it's genuinely appreciated and helps keep the features coming.

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Buy%20me%20a%20coffee-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/mobreadmeo)

<br>

<!-- GETTING STARTED -->
## How To Contribute
> Only needed if you want to work on the code — none of this is required to [just install it](https://github.com/mobread/MobreadModernDock/releases).

### Project architecture

The project follows a layered architecture:

- `MobreadModernDock.Core`: Domain models, application services, widget definitions, and i18n (portable, no OS deps)
- `MobreadModernDock.Infrastructure.Windows`: Windows-specific adapters (Win32 interop, UI Automation, icon extraction, performance counters, JSON persistence, registry auto-start)
- `MobreadModernDock`: Avalonia UI (dock view, settings window, widget providers, view models)
- `MobreadModernDock.Tests`: xUnit test suite

`App.axaml.cs` composes these dependencies and injects them into the view models. New widgets implement `IWidgetProvider` and register in `WidgetRegistry`.

### Prerequisites

- **.NET 9 SDK**
- **Git**
- An IDE (Visual Studio 2022 or Rider recommended) — optional

### Run locally

```bash
cd dotnet
dotnet restore
dotnet run --project src/MobreadModernDock
```

### Build a release `.exe`

```bash
cd dotnet
dotnet build src/MobreadModernDock -c Release
# → src/MobreadModernDock/bin/Release/net9.0-windows/MobreadModernDock.exe
```

### Run the tests

```bash
cd dotnet
dotnet test tests/MobreadModernDock.Tests -c Release
```

### Build the installer

Requires the [WiX Toolset v4](https://wixtoolset.org/) CLI (`dotnet tool install --global wix`).

```powershell
cd dotnet\installer
.\build.ps1 -Version 1.3.0
```

<br>

## License

GPL-3.0 — see [`LICENSE`](LICENSE). Original work © [Arthur Deka](https://github.com/arthurdeka) / Cedro Software; modifications © mobread.
