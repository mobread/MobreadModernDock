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
| [`midnight.mbtheme`](midnight.mbtheme) | Deep blue, heavily rounded, soft acrylic |
| [`paper.mbtheme`](paper.mbtheme) | Light and flat, for light-mode desktops |
| [`slim.mbtheme`](slim.mbtheme) | Minimal height — small icons, no padding, square |
| [`neon.mbtheme`](neon.mbtheme) | Dark violet with a strong magnification bounce |

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
  "blurMode": "acrylic",       // "none" | "blur" | "acrylic"
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

- `blurMode` is a DWM effect drawn behind the window, so it competes with a
  very opaque `dockTransparency`. Pair `"acrylic"` with a value around
  `0.5`–`0.7` to actually see it.
- `magnifyIcons` only applies to a single-row dock (`dockRows: 1`).
- Widgets follow the dock's colour, transparency and rounding, but not the
  other fields.
