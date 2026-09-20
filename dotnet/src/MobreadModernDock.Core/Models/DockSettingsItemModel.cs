namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Direct port of DockSettingsItemModel. The serialized "path" property
/// holds the icon resource path; "label" defaults to "Settings".
/// </summary>
public class DockSettingsItemModel : DockItem
{
    public string Label { get; set; } = "Settings";
    public string Path { get; set; } = "/com/github/mobread/mobreadmoderndock/icons/settings.png";

    [JsonIgnore]
    public DockItemType Type => DockItemType.SETTINGS;

    public DockSettingsItemModel() { }
}
