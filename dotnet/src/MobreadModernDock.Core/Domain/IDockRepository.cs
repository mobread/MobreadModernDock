namespace MobreadModernDock.Core.Domain;

/// <summary>
/// Persistence port — loads and saves the dock configuration.
/// </summary>
public interface IDockRepository
{
    Models.DockModel Load();
    void Save(Models.DockModel model);
}
