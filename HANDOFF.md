# Handoff — MobreadModernDock

_Saved 2026-09-20 (session 2)._

## Branch state

| Branch | State |
|---|---|
| `main` | Upstream verbatim. |
| `chore/kofi-funding` | Pushed. **[PR #1](https://github.com/mobread/MobreadModernDock/pull/1) open, not yet merged** — merge it to make the Sponsor button live. |
| `feature/mobread-customizations` | Pushed, head **`f9564a5`**. Everything through #5 system monitor. 78/78 tests. |

Untracked docs in the working tree: `CUSTOMIZATIONS.md`, `HANDOFF.md` (this file).

## Ko-fi

- `.github/FUNDING.yml` on `chore/kofi-funding`: `ko_fi: mobreadmeo`. README support section is on the feature branch.
- Open question: whether Sponsor buttons render on **forks**. If not after merging PR #1, detach the fork relationship in Settings › General.

## Item #5 — system monitor widget (DONE, `f9564a5`)

Compiled first try. One runtime fix: GPU always read 0% because `GPU Engine\Utilization Percentage` is a rate counter and the code created a fresh `PerformanceCounter` per sample (first `NextValue()` on a rate counter is always 0). Counters now persist in a dictionary, re-enumerated every 5 s. Verified via UIA: `CPU 6% | RAM 22.4/64 GB | GPU 2% | NET ↓4K ↑6K`.

A `sysmon` test widget (`id: sysmontest0001`) is in the live `%APPDATA%\MobreadModernDock\config.json`; a pre-change backup is at `config.json.bak`.

## Remaining feature batch

6. media now-playing · 7. weather · 8. quick-launch grid · 9. calendar

## Next session

Load the `mobread-modern-dock-dev` skill first. Then: _"continue the 1–9 feature batch from #6."_

Pattern for a new widget (see `Widgets/SystemMonitor/` for the template):
1. `WidgetTypes.X = "key"` in `WidgetService.cs`
2. `Widgets/X/XWidgetProvider.cs` implementing `IWidgetProvider` (CreateView + CreateSettingsView + DefaultSettings)
3. Register in `WidgetRegistry.CreateDefault()`
4. i18n keys `widget.type.<key>` + `settings.widget.<key>.*` in all 21 bundles (copy `$LOCALAPPDATA/hermes/cache/scratch/i18n_sysmon.py`)
5. Any OS access goes through a `Core/Domain/I*Gateway` + `Infrastructure.Windows/Adapters/*` impl, wired in `AppServices` + `App.axaml.cs`
