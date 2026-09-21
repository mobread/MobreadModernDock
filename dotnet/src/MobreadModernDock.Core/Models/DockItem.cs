namespace MobreadModernDock.Core.Models;

using System.Globalization;
using System.Text.Json.Serialization;

/// <summary>
/// Polymorphic dock-item interface. The <c>@type</c> discriminator property
/// distinguishes the subtypes in config.json: programItem, folderItem,
/// windowsModuleItem, settingsItem, separatorItem.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@type")]
[JsonDerivedType(typeof(DockProgramItemModel), "programItem")]
[JsonDerivedType(typeof(DockFolderItemModel), "folderItem")]
[JsonDerivedType(typeof(DockWindowsModuleItemModel), "windowsModuleItem")]
[JsonDerivedType(typeof(DockSettingsItemModel), "settingsItem")]
[JsonDerivedType(typeof(DockSeparatorItemModel), "separatorItem")]
public interface DockItem
{
    string Label { get; set; }
    string Path { get; set; }

    /// <summary>
    /// Optional user-chosen icon (.ico/.png/.exe) overriding the one resolved
    /// from the target. Null for the default. Omitted from JSON when unset so
    /// pre-existing configs round-trip unchanged.
    /// </summary>
    [JsonPropertyName("customIcon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CustomIcon { get; set; }

    /// <summary>Not serialized — resolved from the concrete type.</summary>
    [JsonIgnore]
    DockItemType Type { get; }
}
