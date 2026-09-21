namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The serialized "path" property
/// holds the folder path; FolderPath is a non-serialized convenience alias.
/// </summary>
public class DockFolderItemModel : DockItem
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";

    [JsonPropertyName("customIcon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomIcon { get; set; }

    [JsonIgnore]
    public string FolderPath => Path;

    [JsonIgnore]
    public DockItemType Type => DockItemType.FOLDER;

    public DockFolderItemModel() { }

    public DockFolderItemModel(string label, string folderPath)
    {
        Label = label;
        Path = folderPath;
    }
}
