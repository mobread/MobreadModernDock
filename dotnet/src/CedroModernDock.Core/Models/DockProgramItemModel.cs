namespace CedroModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Direct port of DockProgramItemModel. The serialized "path" property
/// holds the executable path; the ExecutablePath accessor is a non-serialized
/// convenience alias (matching the Java @JsonIgnore getExecutablePath).
/// </summary>
public class DockProgramItemModel : DockItem
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>
    /// Optional command-line arguments, e.g. from a .lnk shortcut. Omitted
    /// from JSON when empty so pre-existing configs round-trip unchanged.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonIgnore]
    public string ExecutablePath => Path;

    [JsonIgnore]
    public DockItemType Type => DockItemType.PROGRAM;

    public DockProgramItemModel() { }

    public DockProgramItemModel(string label, string exePath, string? arguments = null)
    {
        Label = label;
        Path = exePath;
        Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments;
    }
}
