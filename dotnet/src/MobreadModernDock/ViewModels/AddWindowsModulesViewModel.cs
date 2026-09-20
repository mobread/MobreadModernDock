using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.ViewModels;

/// <summary>One selectable Windows module: display name + icon.</summary>
public sealed record WindowsModuleEntry(string Name, Bitmap? Icon);

/// <summary>ViewModel for the Add Windows Modules modal. Port of AddWindowsModulesModalController.</summary>
public class AddWindowsModulesViewModel : ViewModelBase
{
    private static readonly string[] ModuleIds = { "start", "mypc", "trash", "ctrlpnl", "pconfig" };

    private readonly AppServices _appServices;
    private readonly Action _dockRefreshAction;
    private int _selectedIndex = -1;

    public ObservableCollection<WindowsModuleEntry> ModuleNames { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    public string Title => _appServices.LocalizationService.Text("windowsModule.modal.title");
    public string Subtitle => _appServices.LocalizationService.Text("windowsModule.modal.subtitle");
    public string AddButtonText => _appServices.LocalizationService.Text("windowsModule.modal.addSelected");

    public AddWindowsModulesViewModel(AppServices appServices, Action dockRefreshAction)
    {
        _appServices = appServices;
        _dockRefreshAction = dockRefreshAction;
        RefreshModuleNames();
    }

    public void AddSelectedModule()
    {
        if (SelectedIndex < 0 || SelectedIndex >= ModuleIds.Length)
            return;

        string moduleId = ModuleIds[SelectedIndex];
        string defaultLabel = moduleId switch
        {
            "start" => "Start Menu",
            "mypc" => "My Computer",
            "trash" => "Recycle Bin",
            "ctrlpnl" => "Control Panel",
            "pconfig" => "Settings",
            _ => moduleId
        };

        _appServices.DockService.AddItem(new DockWindowsModuleItemModel(defaultLabel, moduleId));
        _dockRefreshAction();
    }

    public void RefreshModuleNames()
    {
        var loc = _appServices.LocalizationService;
        ModuleNames.Clear();
        foreach (var id in ModuleIds)
        {
            string name = id switch
            {
                "start" => loc.Text("windowsModule.startMenu"),
                "mypc" => loc.Text("windowsModule.myComputer"),
                "trash" => loc.Text("windowsModule.recycleBin"),
                "ctrlpnl" => loc.Text("windowsModule.controlPanel"),
                "pconfig" => loc.Text("windowsModule.settings"),
                _ => id
            };
            ModuleNames.Add(new WindowsModuleEntry(name, LoadModuleIcon(id)));
        }
    }

    private static Bitmap? LoadModuleIcon(string moduleId)
        => IconLoader.LoadWindowsModuleIcon(moduleId);
}
