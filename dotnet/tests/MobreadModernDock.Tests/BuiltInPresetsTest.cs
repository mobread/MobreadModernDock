namespace MobreadModernDock.Tests;

using System.Text.Json;
using MobreadModernDock.Core.Models;

/// <summary>
/// The shipped presets and the gallery theme files are the same data in two
/// places; these keep them honest and in range.
/// </summary>
public class BuiltInPresetsTest
{
    [Fact]
    public void NamesAreUnique()
    {
        var names = AppearancePreset.BuiltIns().Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryBuiltInIsAlreadyWithinTheUiRanges()
    {
        // Sanitize() clamps; a built-in that changes under it ships an
        // out-of-range value the Settings UI could not have produced.
        foreach (var p in AppearancePreset.BuiltIns())
        {
            var before = JsonSerializer.Serialize(p);
            p.Sanitize();
            Assert.Equal(before, JsonSerializer.Serialize(p));
        }
    }

    [Fact]
    public void PalettePresetsTintWithAReadableAccent()
    {
        // The tint multiplies icon luminance, so a dark accent would make
        // every icon a dark smudge. Require a reasonably bright accent.
        foreach (var p in AppearancePreset.BuiltIns().Where(p => p.TintIcons))
        {
            var rgb = p.TintColorRGB.Split(',', StringSplitOptions.TrimEntries).Take(3).Select(int.Parse).ToArray();
            int max = rgb.Max();
            Assert.True(max >= 150, $"{p.Name}: tint {p.TintColorRGB} is too dark to keep icons readable");
        }
    }

    [Fact]
    public void GalleryThemeFilesMatchTheirBuiltIn()
    {
        string dir = FindThemesDir();
        if (dir == null) return; // running outside the repo
        var builtIns = AppearancePreset.BuiltIns().ToDictionary(p => p.Name);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        int checkedFiles = 0;
        foreach (var file in Directory.GetFiles(dir, "*.mbtheme"))
        {
            var theme = JsonSerializer.Deserialize<AppearancePreset>(File.ReadAllText(file), opts)!;
            if (!builtIns.TryGetValue(theme.Name, out var builtIn)) continue; // gallery-only theme
            theme.Author = builtIn.Author;
            Assert.Equal(JsonSerializer.Serialize(builtIn), JsonSerializer.Serialize(theme));
            checkedFiles++;
        }
        Assert.True(checkedFiles >= 16, $"expected the generated gallery files, found {checkedFiles}");
    }

    private static string? FindThemesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "themes");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
