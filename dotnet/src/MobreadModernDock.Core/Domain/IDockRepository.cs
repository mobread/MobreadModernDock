namespace MobreadModernDock.Core.Domain;

/// <summary>
/// Persistence port — loads and saves the dock configuration.
/// Direct port of the Java DockRepository interface.
/// </summary>
public interface IDockRepository
{
    Models.DockModel Load();
    void Save(Models.DockModel model);
}
