namespace MobreadModernDock.Core.Models;

/// <summary>What a dock item is, and therefore what clicking it does.</summary>
public enum DockItemType
{
    PROGRAM,
    FOLDER,
    WINDOWS_MODULE,
    SETTINGS,

    /// <summary>A non-interactive divider the user can place between icons.</summary>
    SEPARATOR
}
