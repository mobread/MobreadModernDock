using Avalonia.Controls;
using MobreadModernDock.Core.Application;
using MobreadModernDock.ViewModels;

namespace MobreadModernDock.Views;

public partial class AcknowledgementsWindow : Window
{
    public AcknowledgementsWindow()
    {
        InitializeComponent();
    }

    public static void Open(Window owner, LocalizationService localizationService)
    {
        var window = new AcknowledgementsWindow
        {
            DataContext = new AcknowledgementsViewModel(localizationService)
        };
        window.ShowDialog(owner);
    }
}
