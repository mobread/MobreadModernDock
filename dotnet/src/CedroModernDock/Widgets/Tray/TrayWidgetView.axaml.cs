using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CedroModernDock.Widgets.Tray;

public partial class TrayWidgetView : UserControl
{
    public TrayWidgetView()
    {
        InitializeComponent();
    }

    private void OnIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TrayIconViewModel vm })
            vm.Activate();
    }

    private void OnIconContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button { DataContext: TrayIconViewModel vm })
        {
            e.Handled = true;
            vm.ShowContextMenu();
        }
    }
}
