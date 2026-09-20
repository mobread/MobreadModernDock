namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>
/// Direct port of DockService. Holds the dock model loaded from the repository
/// and persists changes on every mutating operation.
/// </summary>
public class DockService
{
    private readonly IDockRepository _repository;
    private readonly DockModel _dock;

    public DockService(IDockRepository repository)
    {
        _repository = repository;
        _dock = repository.Load();
        // Normalize configs from older versions / hand edits: gear goes last.
        if (_dock.KeepSettingsLast())
            SaveChanges();
    }

    public DockModel GetDock() => _dock;

    public List<DockItem> GetItems() => _dock.Items;

    public void AddItem(DockItem item)
    {
        _dock.AddItem(item);
        SaveChanges();
    }

    public void RemoveItem(int index)
    {
        _dock.RemoveItem(index);
        SaveChanges();
    }

    public void SwapItems(int firstItemIdx, int secondItemIdx)
    {
        _dock.SwapItems(firstItemIdx, secondItemIdx);
        SaveChanges();
    }

    public void MoveItem(int fromIndex, int toIndex)
    {
        _dock.MoveItem(fromIndex, toIndex);
        SaveChanges();
    }

    public void SetDockPosition(double positionX, double positionY)
    {
        _dock.SetDockPosition(
            DockPositioningService.SnapToPixel(positionX),
            DockPositioningService.SnapToPixel(positionY));
        SaveChanges();
    }

    public void SaveChanges() => _repository.Save(_dock);
}
