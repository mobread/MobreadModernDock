namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// A user-placed divider between dock icons. Purely decorative: it has no
/// target, no click action and no running indicator — it just draws a thin
/// line in the dock's flow and can be dragged around like any other item.
///
/// Distinct from the hardcoded divider between pinned items and unpinned
/// running apps, which is part of the dock layout rather than the item list.
/// </summary>
public class DockSeparatorItemModel : DockItem
{
    /// <summary>Unused; separators show no tooltip. Kept for the DockItem contract.</summary>
    public string Label { get; set; } = "";

    /// <summary>Unused; a separator has no target.</summary>
    public string Path { get; set; } = "";

    [JsonPropertyName("customIcon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomIcon { get; set; }

    /// <summary>
    /// Empty space reserved along the dock's main axis, as a fraction of the
    /// icon size (0 = a plain hairline; 0.5 = half an icon of blank space).
    /// Scaling with the icon keeps a spacer proportional when the user
    /// changes icon size, and lets the same config look right on any dock.
    /// Omitted from JSON when 0 so older configs round-trip unchanged.
    /// </summary>
    [JsonPropertyName("spacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Spacing { get; set; }

    /// <summary>
    /// True hides the hairline, turning the item into an invisible gap
    /// (only meaningful together with a non-zero <see cref="Spacing"/>).
    /// Omitted from JSON when false.
    /// </summary>
    [JsonPropertyName("hideLine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideLine { get; set; }

    /// <summary>
    /// This separator's own line colour (<c>#AARRGGBB</c>), or null to follow
    /// the dock-wide separator colour. Omitted from JSON when null.
    /// </summary>
    [JsonPropertyName("color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }

    /// <summary>Presets offered in the separator's context menu, as icon-size fractions.</summary>
    public static readonly double[] SpacingPresets = { 0, 0.25, 0.5, 1.0 };

    /// <summary>Clamps a spacing to something sane (a gap is at most two icons wide).</summary>
    public static double SanitizeSpacing(double spacing) =>
        double.IsFinite(spacing) ? Math.Clamp(spacing, 0, 2) : 0;

    [JsonIgnore]
    public DockItemType Type => DockItemType.SEPARATOR;

    public DockSeparatorItemModel() { }
}
