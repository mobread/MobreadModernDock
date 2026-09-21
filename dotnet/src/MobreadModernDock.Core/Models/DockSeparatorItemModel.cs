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

    [JsonIgnore]
    public DockItemType Type => DockItemType.SEPARATOR;

    public DockSeparatorItemModel() { }
}
