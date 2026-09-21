namespace MobreadModernDock.Core.Application;

using System.Text.Json;
using System.Text.Json.Serialization;
using MobreadModernDock.Core.Models;

/// <summary>
/// The shareable theme format (`.mbtheme`): an <see cref="AppearancePreset"/>
/// serialized on its own, so a look can leave the machine it was made on.
/// Presets already existed but lived only inside config.json, which made them
/// impossible to publish or swap with anyone.
///
/// Parsing is deliberately defensive — these files come from strangers on the
/// internet. <see cref="TryParse"/> never throws, rejects anything that is not
/// recognisably a theme, and clamps every value into the range the Settings UI
/// can undo (<see cref="AppearancePreset.Sanitize"/>).
///
/// The file is plain JSON rather than an archive: it stays diff-able and
/// reviewable in a pull request, which is what makes a community theme gallery
/// practical. Bitmap skins would need a container; that is a later decision,
/// and <see cref="AppearancePreset.SchemaVersion"/> is the hook for it.
/// </summary>
public static class ThemeFile
{
    public const string Extension = ".mbtheme";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes a preset to theme JSON.</summary>
    public static string Serialize(AppearancePreset preset)
    {
        preset.SchemaVersion = AppearancePreset.CurrentSchema;
        return JsonSerializer.Serialize(preset, Options);
    }

    /// <summary>
    /// Parses theme JSON. Returns null when the text is not valid JSON, is
    /// not an object, carries a schema this build cannot read, or has no
    /// theme-ish field at all (which is how an unrelated JSON file is caught:
    /// it would otherwise deserialize to an all-default preset and silently
    /// "apply" as a reset).
    /// </summary>
    public static AppearancePreset? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            // Refuse a file from a future breaking schema rather than
            // applying half of it.
            if (doc.RootElement.TryGetProperty("schemaVersion", out var v)
                && v.TryGetInt32(out int schema)
                && schema > AppearancePreset.CurrentSchema)
                return null;

            if (!LooksLikeTheme(doc.RootElement)) return null;

            var preset = JsonSerializer.Deserialize<AppearancePreset>(json, Options);
            if (preset == null) return null;

            preset.Sanitize();
            return preset;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A real theme carries at least one appearance field. Requiring that
    /// separates "a theme that happens to use defaults" from "some other JSON
    /// file the user picked by mistake".
    /// </summary>
    private static bool LooksLikeTheme(JsonElement root)
    {
        foreach (var name in Markers)
            if (root.TryGetProperty(name, out _)) return true;
        return false;
    }

    private static readonly string[] Markers =
    {
        "dockColorRGB", "iconsSize", "dockTransparency", "dockBorderRounding",
        // "blurMode" no longer maps to a setting (the backdrop feature was
        // removed), but themes in the wild still carry it — keep recognising
        // it so those files are still identified as themes.
        "spacingBetweenIcons", "blurMode", "dockPadding", "tintColorRGB",
        "globalOpacity", "dockRows", "magnifyIcons", "magnifyScale",
    };

    /// <summary>
    /// A file name for a theme, with anything the shell would reject stripped.
    /// </summary>
    public static string SuggestedFileName(string themeName)
    {
        string safe = string.IsNullOrWhiteSpace(themeName) ? "theme" : themeName.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '-');
        safe = safe.Replace(' ', '-').ToLowerInvariant();
        if (safe.Length > 40) safe = safe[..40];
        return $"{safe}{Extension}";
    }
}
