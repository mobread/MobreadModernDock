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
            _ => ""
        };

        Label = label;
        Module = module;
    }
}
