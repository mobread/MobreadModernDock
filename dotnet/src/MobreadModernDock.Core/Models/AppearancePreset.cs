namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// #11 A named snapshot of the appearance settings. Applying one overwrites
/// those fields on the DockModel; positions, items and widgets are untouched.
///
/// This doubles as the **theme format**: a preset written to a `.mbtheme`
/// file is what users share (see <c>ThemeTransfer</c>). Anything that belongs
/// to "how the dock looks" therefore belongs here — a field left out is one
/// that silently keeps the user's own value when a theme is applied, which
/// reads as the theme being broken.
/// </summary>
public sealed class AppearancePreset
{
    /// <summary>
    /// Theme-file schema version, written on export and checked on import.
    /// Bump only for a *breaking* change: new optional fields do not need it,
    /// because a file that omits one just keeps the default below.
    /// </summary>
    public const int CurrentSchema = 1;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchema;
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>Optional credit line, preserved across an export/import round trip.</summary>
    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; set; }

    [JsonPropertyName("iconsSize")] public int IconsSize { get; set; } = 24;
    [JsonPropertyName("spacingBetweenIcons")] public int SpacingBetweenIcons { get; set; }
    [JsonPropertyName("dockRows")] public int DockRows { get; set; } = 1;
    [JsonPropertyName("dockTransparency")] public double DockTransparency { get; set; } = 0.3;
    [JsonPropertyName("globalOpacity")] public double GlobalOpacity { get; set; } = 1.0;
    [JsonPropertyName("dockBorderRounding")] public int DockBorderRounding { get; set; } = 10;
    [JsonPropertyName("dockColorRGB")] public string DockColorRGB { get; set; } = "0, 0, 0, ";
    [JsonPropertyName("tintIcons")] public bool TintIcons { get; set; }
    [JsonPropertyName("tintColorRGB")] public string TintColorRGB { get; set; } = "0, 80, 140";
    [JsonPropertyName("verticalDock")] public bool VerticalDock { get; set; }

    // --- Added in 1.0.3. A theme omitting these keeps the defaults, so files
    //     written before they existed still load unchanged.

    /// <summary>
    /// Backdrop material ("none" / "blur" / "acrylic"). Without this the
    /// built-in "Glass" preset set a pale colour but left the blur off — the
    /// one preset named after an effect did not switch that effect on.
    /// </summary>
    [JsonPropertyName("blurMode")] public string BlurMode { get; set; } = "none";

    /// <summary>Padding inside the dock bar, px. Drives the bar's overall thickness.</summary>
    [JsonPropertyName("dockPadding")] public int DockPadding { get; set; } = 10;

    [JsonPropertyName("magnifyIcons")] public bool MagnifyIcons { get; set; }
    [JsonPropertyName("magnifyScale")] public double MagnifyScale { get; set; } = 1.6;

    public static AppearancePreset Capture(string name, DockModel d) => new()
    {
        SchemaVersion = CurrentSchema,
        Name = name,
        IconsSize = d.IconsSize,
        SpacingBetweenIcons = d.SpacingBetweenIcons,
        DockRows = d.DockRows,
        DockTransparency = d.DockTransparency,
        GlobalOpacity = d.GlobalOpacity,
        DockBorderRounding = d.DockBorderRounding,
        DockColorRGB = d.DockColorRGB,
        TintIcons = d.TintIcons,
        TintColorRGB = d.TintColorRGB,
        VerticalDock = d.VerticalDock,
        BlurMode = d.BlurMode,
        DockPadding = d.DockPadding,
        MagnifyIcons = d.MagnifyIcons,
        MagnifyScale = d.MagnifyScale,
    };

    public void ApplyTo(DockModel d)
    {
        d.IconsSize = IconsSize;
        d.SpacingBetweenIcons = SpacingBetweenIcons;
        d.DockRows = DockRows;
        d.DockTransparency = DockTransparency;
        d.GlobalOpacity = GlobalOpacity;
        d.DockBorderRounding = DockBorderRounding;
        d.DockColorRGB = DockColorRGB;
        d.TintIcons = TintIcons;
        d.TintColorRGB = TintColorRGB;
        d.VerticalDock = VerticalDock;
        d.BlurMode = BlurMode;
        d.DockPadding = DockPadding;
        d.MagnifyIcons = MagnifyIcons;
        d.MagnifyScale = MagnifyScale;
    }

    /// <summary>
    /// Clamps every field into the range the Settings UI allows. Applied on
    /// import: a hand-edited or corrupt theme must not be able to push the
    /// dock somewhere the UI cannot bring it back from — a 500px icon, or a
    /// fully transparent bar with no visible handle to grab.
    /// </summary>
    public void Sanitize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Imported theme" : Name.Trim();
        if (Name.Length > 60) Name = Name[..60];
        if (Author is { Length: > 60 }) Author = Author[..60];

        IconsSize = Math.Clamp(IconsSize, 16, 128);
        SpacingBetweenIcons = Math.Clamp(SpacingBetweenIcons, 0, 40);
        DockRows = Math.Clamp(DockRows, 1, 4);
        DockTransparency = Math.Clamp(DockTransparency, 0.0, 1.0);
        GlobalOpacity = Math.Clamp(GlobalOpacity, 0.2, 1.0);
        DockBorderRounding = Math.Clamp(DockBorderRounding, 0, 60);
        DockPadding = Math.Clamp(DockPadding, 0, 40);
        MagnifyScale = Math.Clamp(MagnifyScale, 1.0, 2.5);
        BlurMode = BlurMode is "blur" or "acrylic" ? BlurMode : "none";
        DockColorRGB = SanitizeRgb(DockColorRGB, "0, 0, 0, ");
        TintColorRGB = SanitizeRgb(TintColorRGB, "0, 80, 140");
    }

    /// <summary>
    /// Keeps an "r, g, b" string only when it really parses to three 0-255
    /// components, so a malformed colour never reaches the brush parser.
    /// </summary>
    private static string SanitizeRgb(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return fallback;
        for (int i = 0; i < 3; i++)
            if (!byte.TryParse(parts[i], out _)) return fallback;
        return value;
    }

    /// <summary>Ships a few starting points so the feature isn't an empty list.</summary>
    public static List<AppearancePreset> BuiltIns() => new()
    {
        new() { Name = "Classic dark", IconsSize = 40, SpacingBetweenIcons = 4, DockTransparency = 0.3, DockBorderRounding = 16, DockColorRGB = "0, 0, 0, " },
        // Named after an effect, so it has to switch that effect on.
        new() { Name = "Glass", IconsSize = 40, SpacingBetweenIcons = 6, DockTransparency = 0.75, DockBorderRounding = 22, DockColorRGB = "255, 255, 255, ", BlurMode = "acrylic" },
        new() { Name = "Compact", IconsSize = 28, SpacingBetweenIcons = 0, DockTransparency = 0.2, DockBorderRounding = 8, DockColorRGB = "20, 20, 20, ", DockPadding = 5 },
        new() { Name = "Midnight blue", IconsSize = 40, SpacingBetweenIcons = 4, DockTransparency = 0.35, DockBorderRounding = 14, DockColorRGB = "10, 25, 60, ", TintIcons = true, TintColorRGB = "80, 140, 220" },
        new() { Name = "macOS", IconsSize = 44, SpacingBetweenIcons = 6, DockTransparency = 0.6, DockBorderRounding = 24, DockColorRGB = "40, 40, 45, ", BlurMode = "acrylic", DockPadding = 8, MagnifyIcons = true, MagnifyScale = 1.8 },
    };
}
