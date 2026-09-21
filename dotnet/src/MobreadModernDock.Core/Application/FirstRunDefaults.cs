namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Models;

/// <summary>
/// Builds the item list a brand-new config starts with. Before this, a first
/// run produced a dock holding nothing but the Settings gear, which reads as
/// "the app is broken" rather than "the app is empty".
///
/// The seed is the user's own taskbar pins (so the dock looks familiar on the
/// very first launch) followed by a divider and two Windows modules. The
/// composition is a pure function of the candidate lists so it can be tested
/// without touching the shell; <c>Infrastructure.Windows</c> supplies the
/// real pins.
/// </summary>
public static class FirstRunDefaults
{
    /// <summary>
    /// Cap on seeded programs. A taskbar with 30 pins would otherwise produce
    /// a dock wider than the screen on its first appearance.
    /// </summary>
    public const int MaxSeededPrograms = 8;

    /// <summary>Windows modules appended after the programs, in order.</summary>
    public static readonly string[] SeededModules = { "mypc", "trash" };

    /// <summary>
    /// Composes the default items, <b>excluding</b> the Settings gear —
    /// <see cref="DockModel.LoadDefaultItems"/> appends that last.
    /// </summary>
    /// <param name="taskbarPins">Programs resolved from the taskbar's pins.</param>
    /// <param name="fallbackPrograms">
    /// Used only when <paramref name="taskbarPins"/> yields nothing (a profile
    /// with no pins, or a locked-down machine).
    /// </param>
    /// <param name="excludeExecutableName">
    /// File name whose pin is dropped — the dock's own exe, since pinning the
    /// dock to the dock is noise.
    /// </param>
    public static List<DockItem> Compose(
        IEnumerable<DockProgramItemModel>? taskbarPins,
        IEnumerable<DockProgramItemModel>? fallbackPrograms = null,
        string? excludeExecutableName = "MobreadModernDock.exe")
    {
        var programs = Select(taskbarPins, excludeExecutableName);
        if (programs.Count == 0)
            programs = Select(fallbackPrograms, excludeExecutableName);

        var items = new List<DockItem>();
        items.AddRange(programs);
        // A leading divider would look like a rendering glitch, so it only
        // appears when there is something to divide from the modules.
        if (programs.Count > 0)
            items.Add(new DockSeparatorItemModel());
        foreach (var module in SeededModules)
            items.Add(new DockWindowsModuleItemModel(DefaultModuleLabel(module), module));
        return items;
    }

    /// <summary>
    /// English labels matching the Add Windows Module picker. Dock item labels
    /// are user-editable text stored in the config, not i18n keys, so they are
    /// not localized here.
    /// </summary>
    public static string DefaultModuleLabel(string moduleId) => moduleId switch
    {
        "start" => "Start Menu",
        "mypc" => "My Computer",
        "trash" => "Recycle Bin",
        "ctrlpnl" => "Control Panel",
        "pconfig" => "Settings",
        _ => moduleId
    };

    private static List<DockProgramItemModel> Select(
        IEnumerable<DockProgramItemModel>? candidates, string? excludeExecutableName)
    {
        var result = new List<DockProgramItemModel>();
        if (candidates == null) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.ExecutablePath)) continue;
            if (excludeExecutableName != null
                && string.Equals(System.IO.Path.GetFileName(candidate.ExecutablePath),
                                 excludeExecutableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(candidate.ExecutablePath)) continue;
            result.Add(candidate);
            if (result.Count == MaxSeededPrograms) break;
        }
        return result;
    }
}
