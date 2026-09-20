namespace CedroModernDock.Core.Domain;

/// <summary>
/// One icon in the Windows notification area ("system tray").
/// <see cref="Key"/> is stable across refreshes for the same icon (used to
/// reuse view models); <see cref="IconPng"/> is the shell's cached PNG
/// snapshot of the icon, when available.
/// </summary>
public sealed record TrayIconInfo(
    string Key,
    string Name,
    string? ExecutablePath,
    byte[]? IconPng,
    bool IsSystemIcon);

/// <summary>
/// Port for reading and interacting with the OS notification area.
/// </summary>
public interface ITrayIconGateway
{
    /// <summary>Snapshot of the currently visible tray icons, left to right.</summary>
    List<TrayIconInfo> GetIcons();

    /// <summary>Left-click (activate) the icon.</summary>
    bool Activate(string key);

    /// <summary>Right-click (context menu) the icon.</summary>
    bool ShowContextMenu(string key);
}
