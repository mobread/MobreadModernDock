namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Direct port of DockWindowsModuleItemModel. The "module" field identifies
/// which built-in Windows surface this item opens (start, mypc, trash, ctrlpnl, pconfig).
/// The icon path is derived from the module in the constructor.
/// </summary>
public class DockWindowsModuleItemModel : DockItem
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";

    [JsonPropertyName("customIcon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomIcon { get; set; }

    public string Module { get; set; } = "";

    [JsonIgnore]
    public DockItemType Type => DockItemType.WINDOWS_MODULE;

    public DockWindowsModuleItemModel() { }

    public DockWindowsModuleItemModel(string label, string module)
    {
        Path = module switch
        {
            "start" => "/com/github/mobread/mobreadmoderndock/icons/start_menu.png",
            "mypc" => "/com/github/mobread/mobreadmoderndock/icons/my_computer.png",
            "trash" => "/com/github/mobread/mobreadmoderndock/icons/trash.png",
            "ctrlpnl" => "/com/github/mobread/mobreadmoderndock/icons/control.png",
            "pconfig" => "/com/github/mobread/mobreadmoderndock/icons/windows_settings.png",
            "shutdown" => "/com/github/mobread/mobreadmoderndock/icons/power_shutdown.png",
            "restart" => "/com/github/mobread/mobreadmoderndock/icons/power_restart.png",
            "signout" => "/com/github/mobread/mobreadmoderndock/icons/power_signout.png",
            "sleep" => "/com/github/mobread/mobreadmoderndock/icons/power_sleep.png",
            "lock" => "/com/github/mobread/mobreadmoderndock/icons/power_lock.png",
            _ => ""
        };

        Label = label;
        Module = module;
    }

    /// <summary>
    /// Modules that end or suspend the session. They are grouped separately in
    /// the picker and get a confirmation prompt before running.
    /// </summary>
    public static readonly string[] PowerModules = { "shutdown", "restart", "signout", "sleep", "lock" };

    /// <summary>True when the module id is one of the power actions.</summary>
    public static bool IsPowerModule(string module) => Array.IndexOf(PowerModules, module) >= 0;

    /// <summary>Power actions that are irreversible enough to confirm first.</summary>
    public static bool NeedsConfirmation(string module) =>
        module is "shutdown" or "restart" or "signout";
}
