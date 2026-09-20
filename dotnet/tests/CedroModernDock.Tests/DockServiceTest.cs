namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>Direct port of DockServiceTest.java, extended for the "Settings gear stays last" rule.</summary>
public class DockServiceTest
{
    [Fact]
    public void SnapsSavedDockCoordinatesToWholePixels()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);

        service.SetDockPosition(718.5, 28.2);

        DockModel savedDock = repository.SavedModel!;
        Assert.Equal(719.0, savedDock.DockPositionX);
        Assert.Equal(28.0, savedDock.DockPositionY);
    }

    [Fact]
    public void MoveItemMovesAnItemForwardIntoTheGivenGap()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("X", @"C:\tools\x.exe")); // [0]
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [1]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [2]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [3]

        // Move index 0 into the gap after item "B" (gap index 3).
        service.MoveItem(0, 3);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "B", "X", "C" }, labels);
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void MoveItemMovesAnItemBackwardIntoTheGivenGap()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [0]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [1]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [2]
        service.AddItem(new DockProgramItemModel("D", @"C:\tools\d.exe")); // [3]

        // Move index 3 into the gap before item "B" (gap index 1).
        service.MoveItem(3, 1);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "D", "B", "C" }, labels);
        Assert.Same(repository.SavedModel, service.GetDock());
    }

    [Fact]
    public void MoveItemToTheSameGapLeavesTheOrderUntouched()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe"));

        service.MoveItem(1, 1);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "A", "B" }, labels);
    }

    [Fact]
    public void MoveItemClampsAnOutOfRangeGapToTheListBounds()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe")); // [0]
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe")); // [1]
        service.AddItem(new DockProgramItemModel("C", @"C:\tools\c.exe")); // [2]

        // Gap index way past the end behaves like the last gap (after "C").
        service.MoveItem(0, 99);

        var labels = service.GetItems().Select(i => i.Label).ToList();
        Assert.Equal(new[] { "B", "C", "A" }, labels);
    }

    [Fact]
    public void SettingsItemStaysLastWhenItemsAreAddedAfterIt()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockSettingsItemModel());
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe"));

        Assert.Equal(new[] { "A", "B", "Settings" }, service.GetItems().Select(i => i.Label));
    }

    [Fact]
    public void SettingsItemCannotBeMovedAwayFromTheEnd()
    {
        var repository = new InMemoryDockRepository();
        var service = new DockService(repository);
        service.AddItem(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        service.AddItem(new DockProgramItemModel("B", @"C:\tools\b.exe"));
        service.AddItem(new DockSettingsItemModel());

        service.MoveItem(2, 0); // try to drag the gear to the front
        Assert.Equal(new[] { "A", "B", "Settings" }, service.GetItems().Select(i => i.Label));

        service.MoveItem(0, 3); // try to drop "A" after the gear
        Assert.Equal(new[] { "B", "A", "Settings" }, service.GetItems().Select(i => i.Label));
    }

    [Fact]
    public void LoadNormalizesAConfigWithSettingsInTheMiddle()
    {
        var model = new DockModel();
        model.Items.Add(new DockProgramItemModel("A", @"C:\tools\a.exe"));
        model.Items.Add(new DockSettingsItemModel());
        model.Items.Add(new DockProgramItemModel("B", @"C:\tools\b.exe"));
        var repository = new InMemoryDockRepository(model);

        var service = new DockService(repository);

        Assert.Equal(new[] { "A", "B", "Settings" }, service.GetItems().Select(i => i.Label));
        Assert.Same(model, repository.SavedModel); // normalized order was persisted
    }

    private sealed class InMemoryDockRepository : IDockRepository
    {
        private readonly DockModel _model;
        public DockModel? SavedModel { get; private set; }

        public InMemoryDockRepository(DockModel? model = null) => _model = model ?? new DockModel();

        public DockModel Load()
        {
            SavedModel ??= _model;
            return _model;
        }

        public void Save(DockModel model) => SavedModel = model;
    }
}
