namespace MobreadModernDock.Infrastructure.Windows.Native;

using MobreadModernDock.Core.Models;

/// <summary>
/// Reads the taskbar's pinned shortcuts. Windows keeps them as .lnk files in
/// <c>%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar</c>
/// — the same location for Win10 and Win11, including the pins a fresh
/// Windows install ships with.
///
/// Used both by Settings › "Import Taskbar Pins" and by the first-run seed,
/// so the two can never drift apart.
/// </summary>
public static class TaskbarPinImporter
{
    public static string PinsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    /// <summary>
    /// Every pin that resolves to an existing executable, in the shell's own
    /// file order (alphabetical), de-duplicated by target exe. Never throws:
    /// an unreadable profile yields an empty list.
    /// </summary>
    public static IReadOnlyList<DockProgramItemModel> Enumerate()
    {
        string dir = PinsDirectory;
        if (!Directory.Exists(dir)) return Array.Empty<DockProgramItemModel>();

        string[] links;
        try
        {
            links = Directory.GetFiles(dir, "*.lnk");
        }
        catch (IOException)
        {
            return Array.Empty<DockProgramItemModel>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DockProgramItemModel>();
        }

        Array.Sort(links, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<DockProgramItemModel>();
        foreach (var lnk in links)
        {
            var item = ProgramItemFactory.Create(lnk);
            if (item == null || !seen.Add(item.ExecutablePath)) continue;
            items.Add(item);
        }
        return items;
    }
}
