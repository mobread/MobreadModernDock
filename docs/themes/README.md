# Themes

A theme is a small JSON file (`.mbtheme`) describing how the dock *looks* —
colour, size, roundness, backdrop, spacing. It never contains your pinned apps,
window position or widgets, so applying one is safe: it changes the appearance
and nothing else.

## Using a theme

- **Import** — Settings › Appearance › *Import theme…*, or just **drag the
  `.mbtheme` file onto the dock**.
- **Export** — Settings › Appearance › *Export theme…* writes your current look
  to a file you can share.

An imported theme is also saved as a preset, so you can switch back to it later
from the dropdown. Importing a theme whose name you already use adds a numbered
copy rather than overwriting yours.

## The gallery

Drop the file on your dock to try one.

| Theme | Looks like |
|---|---|
| [`midnight.mbtheme`](midnight.mbtheme) | Deep blue, heavily rounded, softly translucent |
| [`paper.mbtheme`](paper.mbtheme) | Light and flat, for light-mode desktops |
| [`slim.mbtheme`](slim.mbtheme) | Minimal height — small icons, no padding, square |
| [`neon.mbtheme`](neon.mbtheme) | Dark violet with a strong magnification bounce |

### Looks from other desktops

| Theme | Looks like |
|---|---|
| [`windows-11.mbtheme`](windows-11.mbtheme) | The stock Win11 taskbar — flat, square, near-opaque |
| [`windows-7-aero.mbtheme`](windows-7-aero.mbtheme) | Pale blue glass, softly rounded |
| [`kde-plasma.mbtheme`](kde-plasma.mbtheme) | Plasma's default panel: near-black, tight, barely rounded |
| [`gnome-dash.mbtheme`](gnome-dash.mbtheme) | A dark rounded pill with big icons, like dash-to-dock |

### Colour palettes

These also tint the icons with the palette's accent, so the whole dock reads as one scheme.

| Theme | Looks like |
|---|---|
| [`nord.mbtheme`](nord.mbtheme) | Polar night bar, frost-blue tinted icons |
| [`dracula.mbtheme`](dracula.mbtheme) | Dracula background with purple-tinted icons |
| [`catppuccin-mocha.mbtheme`](catppuccin-mocha.mbtheme) | Mocha base with mauve icons |
| [`catppuccin-latte.mbtheme`](catppuccin-latte.mbtheme) | The light Catppuccin flavour, mauve icons |
| [`gruvbox-dark.mbtheme`](gruvbox-dark.mbtheme) | Warm dark bar, yellow-tinted icons |
| [`solarized-dark.mbtheme`](solarized-dark.mbtheme) | Base03 bar, cyan icons |
| [`solarized-light.mbtheme`](solarized-light.mbtheme) | Base3 bar, blue icons — for light desktops |
| [`tokyo-night.mbtheme`](tokyo-night.mbtheme) | Deep navy bar, blue icons |
| [`one-dark.mbtheme`](one-dark.mbtheme) | Atom's One Dark, blue icons |
| [`rose-pine.mbtheme`](rose-pine.mbtheme) | Rosé Pine base, rose-tinted icons |
| [`monokai.mbtheme`](monokai.mbtheme) | Monokai bar, green icons |
| [`everforest.mbtheme`](everforest.mbtheme) | Everforest bar, sage-green icons |

**Contributions welcome.** Open a PR adding your `.mbtheme` here plus a row in
this table. Because a theme is plain JSON, it is reviewable in the diff — which
is exactly why the format is not a binary archive.

## Format

```jsonc
{
  "schemaVersion": 1,          // bumped only for breaking changes
  "name": "Midnight",          // shown in the presets dropdown
  "author": "you",             // optional

  "iconsSize": 44,             // 16-128
  "spacingBetweenIcons": 6,    // 0-40
  "dockRows": 1,               // 1-4
  "dockPadding": 8,            // 0-40, space inside the bar
  "dockBorderRounding": 24,    // 0-60
  "dockTransparency": 0.6,     // 0.0-1.0, background fill only
  "globalOpacity": 1.0,        // 0.2-1.0, whole dock incl. icons
  "dockColorRGB": "40, 40, 45, ",
  "tintIcons": false,
  "tintColorRGB": "0, 80, 140",
  "verticalDock": false,
  "magnifyIcons": true,
  "magnifyScale": 1.8          // 1.0-2.5
}
```

Every field is optional — anything you leave out keeps its default, so a
three-line theme is perfectly valid.

Values are clamped to the ranges above on import, so a hand-edited file can't
push the dock somewhere the Settings window cannot bring it back from. A file
that isn't recognisably a theme is rejected outright rather than applied as a
reset.

### Notes

- `blurMode` was removed (the backdrop effect it drove no longer exists).
  Older themes may still carry it; the key is ignored, and such files still
  load. Use `dockTransparency` for a see-through bar.
- `magnifyIcons` only applies to a single-row dock (`dockRows: 1`).
- Widgets follow the dock's colour, transparency and rounding, but not the
  other fields.
