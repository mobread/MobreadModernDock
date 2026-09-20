using Avalonia.Controls;
using Avalonia.Interactivity;
using MobreadModernDock.Core.Application;
using MobreadModernDock.ViewModels;

namespace MobreadModernDock.Views;

public partial class AddWindowsModulesWindow : Window
{
    private AddWindowsModulesViewModel? _viewModel;

    public AddWindowsModulesWindow()
    {
        InitializeComponent();
    }

    public static Task Open(AppServices appServices, Action dockRefreshAction, Window owner)
    {
        var vm = new AddWindowsModulesViewModel(appServices, dockRefreshAction);
        var window = new AddWindowsModulesWindow { DataContext = vm, _viewModel = vm };
        return window.ShowDialog(owner);
    }

    private void OnAddSelected(object? sender, RoutedEventArgs e)
    {
        _viewModel?.AddSelectedModule();
        Close();
    }
}
