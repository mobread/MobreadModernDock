namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// #11 A named snapshot of the appearance settings. Applying one overwrites
/// those fields on the DockModel; positions, items and widgets are untouched.
/// </summary>
public sealed class AppearancePreset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
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

    public static AppearancePreset Capture(string name, DockModel d) => new()
    {
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
    }

    /// <summary>Ships a few starting points so the feature isn't an empty list.</summary>
    public static List<AppearancePreset> BuiltIns() => new()
    {
        new() { Name = "Classic dark", IconsSize = 40, SpacingBetweenIcons = 4, DockTransparency = 0.3, DockBorderRounding = 16, DockColorRGB = "0, 0, 0, " },
        new() { Name = "Glass", IconsSize = 40, SpacingBetweenIcons = 6, DockTransparency = 0.75, DockBorderRounding = 22, DockColorRGB = "255, 255, 255, " },
        new() { Name = "Compact", IconsSize = 28, SpacingBetweenIcons = 0, DockTransparency = 0.2, DockBorderRounding = 8, DockColorRGB = "20, 20, 20, " },
        new() { Name = "Midnight blue", IconsSize = 40, SpacingBetweenIcons = 4, DockTransparency = 0.35, DockBorderRounding = 14, DockColorRGB = "10, 25, 60, ", TintIcons = true, TintColorRGB = "80, 140, 220" },
    };
}
