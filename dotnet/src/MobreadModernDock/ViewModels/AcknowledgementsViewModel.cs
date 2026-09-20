using MobreadModernDock.Core.Application;

namespace MobreadModernDock.ViewModels;

/// <summary>Localized texts for the Acknowledgements window.</summary>
public sealed class AcknowledgementsViewModel
{
    private readonly LocalizationService _loc;

    public AcknowledgementsViewModel(LocalizationService loc) => _loc = loc;

    public string Title => _loc.Text("acknowledgements.title");
    public string Subtitle => _loc.Text("acknowledgements.subtitle");
    public string LibrariesTitle => _loc.Text("acknowledgements.sectionTitle");
}
