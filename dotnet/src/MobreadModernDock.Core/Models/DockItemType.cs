namespace MobreadModernDock.Core.Models;

/// <summary>Direct port of the Java DockItemType enum.</summary>
public enum DockItemType
{
    PROGRAM,
    FOLDER,
    WINDOWS_MODULE,
    SETTINGS,

    /// <summary>A non-interactive divider the user can place between icons.</summary>
    SEPARATOR
}
