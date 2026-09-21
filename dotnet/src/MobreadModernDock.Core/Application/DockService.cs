namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// Holds the dock model loaded from the repository and persists changes on
/// every mutating operation.
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

    /// <summary>
    /// Sets (or clears, with null) the user-chosen icon of the item at
    /// <paramref name="index"/>. The path is stored as given; the icon loader
    /// falls back to the default when the file disappears later.
    /// </summary>
    public void SetCustomIcon(int index, string? iconPath)
    {
        if (index < 0 || index >= _dock.Items.Count) return;
        _dock.Items[index].CustomIcon = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;
        SaveChanges();
    }

    /// <summary>
    /// Inserts an item at a specific gap (0..Count). Used by drag-and-drop
    /// pinning, where the drop position decides the slot. The Settings gear is
    /// kept last regardless of the requested index.
    /// </summary>
    public void InsertItem(DockItem item, int index)
    {
        index = Math.Clamp(index, 0, _dock.Items.Count);
        _dock.Items.Insert(index, item);
        _dock.KeepSettingsLast();
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
