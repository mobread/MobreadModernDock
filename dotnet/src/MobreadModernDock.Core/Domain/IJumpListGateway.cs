namespace MobreadModernDock.Core.Domain;

/// <summary>One entry of an application's jump list: a task or a recent item.</summary>
/// <param name="Title">Menu text as the taskbar would show it.</param>
/// <param name="ExecutablePath">Program the entry runs (usually the app itself).</param>
/// <param name="Arguments">Raw command line for that program.</param>
/// <param name="Description">Tooltip text, when the app supplied one.</param>
public sealed record JumpListEntry(string Title, string ExecutablePath, string Arguments, string? Description);

/// <summary>A named group of jump list entries, in the order the app declared them.</summary>
/// <param name="Name">Custom category name; null for the standard "Tasks" group.</param>
public sealed record JumpListCategory(string? Name, IReadOnlyList<JumpListEntry> Entries)
{
    public bool IsTasks => Name == null;
}

/// <summary>
/// Port for the jump lists Windows keeps per application - the "Tasks" and
/// custom groups the taskbar shows on right-click (New window, New incognito
/// window, a terminal's profiles...).
/// </summary>
public interface IJumpListGateway
{
    /// <summary>
    /// Categories of the jump list whose entries run <paramref name="executablePath"/>,
    /// or an empty list when the app has none. Never throws.
    /// </summary>
    IReadOnlyList<JumpListCategory> GetCategories(string executablePath);
}
