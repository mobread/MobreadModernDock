"""Writes docs/themes/<slug>.mbtheme for every built-in preset (except the
originals that already ship) and rewrites the gallery table in README.md.
Run after editing AppearancePreset.BuiltIns()."""
import json, re, os, subprocess
root = r"C:\Users\micha\claude-projects\MobreadModernDock"
src = open(os.path.join(root, r"dotnet\src\MobreadModernDock.Core\Models\AppearancePreset.cs"), encoding="utf-8").read()
body = src[src.index("BuiltIns() => new()"):]
presets = []
for m in re.finditer(r'new\(\)\s*\{\s*(Name = "[^"]+".*?)\},', body, re.S):
    fields = {}
    for k, v in re.findall(r'(\w+) = ("[^"]*"|[\d.]+|true|false)', m.group(1)):
        fields[k] = v.strip('"') if v.startswith('"') else (v == "true" if v in ("true", "false") else float(v) if "." in v else int(v))
    presets.append(fields)

blurbs = {
    "Windows 11": "The stock Win11 taskbar — flat, square, near-opaque",
    "Windows 7 Aero": "Pale blue glass, softly rounded",
    "KDE Plasma": "Plasma's default panel: near-black, tight, barely rounded",
    "GNOME dash": "A dark rounded pill with big icons, like dash-to-dock",
    "Nord": "Polar night bar, frost-blue tinted icons",
    "Dracula": "Dracula background with purple-tinted icons",
    "Catppuccin Mocha": "Mocha base with mauve icons",
    "Catppuccin Latte": "The light Catppuccin flavour, mauve icons",
    "Gruvbox Dark": "Warm dark bar, yellow-tinted icons",
    "Solarized Dark": "Base03 bar, cyan icons",
    "Solarized Light": "Base3 bar, blue icons — for light desktops",
    "Tokyo Night": "Deep navy bar, blue icons",
    "One Dark": "Atom's One Dark, blue icons",
    "Rosé Pine": "Rosé Pine base, rose-tinted icons",
    "Monokai": "Monokai bar, green icons",
    "Everforest": "Everforest bar, sage-green icons",
}
keymap = {"IconsSize": "iconsSize", "SpacingBetweenIcons": "spacingBetweenIcons", "DockRows": "dockRows",
          "DockTransparency": "dockTransparency", "GlobalOpacity": "globalOpacity", "DockBorderRounding": "dockBorderRounding",
          "DockColorRGB": "dockColorRGB", "TintIcons": "tintIcons", "TintColorRGB": "tintColorRGB", "VerticalDock": "verticalDock",
          "DockPadding": "dockPadding", "MagnifyIcons": "magnifyIcons", "MagnifyScale": "magnifyScale"}
rows = []
for p in presets:
    name = p["Name"]
    if name not in blurbs: continue
    slug = re.sub(r"[^a-z0-9]+", "-", name.lower().replace("é", "e")).strip("-")
    out = {"schemaVersion": 1, "name": name, "author": "mobread"}
    for k, v in p.items():
        if k in keymap: out[keymap[k]] = v
    path = os.path.join(root, "docs", "themes", slug + ".mbtheme")
    with open(path, "w", encoding="ascii", newline="\n") as f:
        json.dump(out, f, indent=2, ensure_ascii=True); f.write("\n")
    rows.append(f"| [`{slug}.mbtheme`]({slug}.mbtheme) | {blurbs[name]} |")
    print("wrote", slug)

readme = os.path.join(root, "docs", "themes", "README.md")
t = open(readme, encoding="utf-8").read()
start = t.index("| Theme | Looks like |")
end = t.index("**Contributions welcome.**")
table = t[start:end].rstrip("\n").splitlines()
existing = [l for l in table if l.startswith("| [`")]
header = table[:2]
looks = [r for r in rows if any(x in r for x in ("windows-11", "windows-7", "kde", "gnome"))]
palettes = [r for r in rows if r not in looks]
new = "\n".join(header + existing) + "\n\n### Looks from other desktops\n\n" + "\n".join(header + looks) + "\n\n### Colour palettes\n\nThese also tint the icons with the palette's accent, so the whole dock reads as one scheme.\n\n" + "\n".join(header + palettes) + "\n\n"
open(readme, "w", encoding="utf-8", newline="\n").write(t[:start] + new + t[end:])
print("readme updated;", len(rows), "themes")
