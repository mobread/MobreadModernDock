namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The serialized "path" property
/// holds the icon resource path; "label" defaults to "Settings".
/// </summary>
public class DockSettingsItemModel : DockItem
{
    public string Label { get; set; } = "Settings";
    public string Path { get; set; } = "/com/github/mobread/mobreadmoderndock/icons/settings.png";

    [JsonPropertyName("customIcon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomIcon { get; set; }

    [JsonIgnore]
    public DockItemType Type => DockItemType.SETTINGS;

    public DockSettingsItemModel() { }
}
