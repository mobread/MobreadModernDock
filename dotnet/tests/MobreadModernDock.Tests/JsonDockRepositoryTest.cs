namespace MobreadModernDock.Tests;

using System.IO;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Persistence;

/// <summary>config.json round-trips, including the polymorphic dock-item subtypes.</summary>
public class JsonDockRepositoryTest
{
    private string _tempDir = null!;

    public JsonDockRepositoryTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MobreadRepoTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void SavesAndLoadsFolderItems()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        var model = new DockModel();
        model.AddItem(new DockFolderItemModel("Projects", @"C:\Users\Arthur Rodrigues\Projects"));

        repository.Save(model);
        DockModel loadedModel = repository.Load();

        Assert.Single(loadedModel.Items);

        DockItem loadedItem = loadedModel.Items[0];
        var folderItem = Assert.IsType<DockFolderItemModel>(loadedItem);
        Assert.Equal(DockItemType.FOLDER, folderItem.Type);
        Assert.Equal("Projects", folderItem.Label);
        Assert.Equal(@"C:\Users\Arthur Rodrigues\Projects", folderItem.FolderPath);
        CleanupTempDir();
    }

    [Fact]
    public void SavesAndLoadsSeparatorItems()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        var model = new DockModel();
        model.AddItem(new DockProgramItemModel("Notepad", @"C:\Windows\notepad.exe"));
        model.AddItem(new DockSeparatorItemModel());
        model.AddItem(new DockFolderItemModel("Projects", @"C:\Projects"));

        repository.Save(model);
        DockModel loadedModel = repository.Load();

        Assert.Equal(3, loadedModel.Items.Count);
        // Order must survive the round trip: a divider is only meaningful in place.
        var separator = Assert.IsType<DockSeparatorItemModel>(loadedModel.Items[1]);
        Assert.Equal(DockItemType.SEPARATOR, separator.Type);
        Assert.IsType<DockProgramItemModel>(loadedModel.Items[0]);
        Assert.IsType<DockFolderItemModel>(loadedModel.Items[2]);

        // The discriminator is what keeps older/newer configs interoperable.
        string json = File.ReadAllText(configPath);
        Assert.Contains("\"@type\": \"separatorItem\"", json);
        CleanupTempDir();
    }

    [Fact]
    public void SeparatorSpacingRoundTripsAndDefaultsStayOffDisk()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        var model = new DockModel();
        model.AddItem(new DockSeparatorItemModel());                                  // plain hairline
        model.AddItem(new DockSeparatorItemModel { Spacing = 0.5 });                  // spacer with line
        model.AddItem(new DockSeparatorItemModel { Spacing = 1.0, HideLine = true }); // invisible gap

        repository.Save(model);
        var loaded = repository.Load().Items.OfType<DockSeparatorItemModel>().ToList();

        Assert.Equal(0, loaded[0].Spacing);
        Assert.False(loaded[0].HideLine);
        Assert.Equal(0.5, loaded[1].Spacing);
        Assert.False(loaded[1].HideLine);
        Assert.Equal(1.0, loaded[2].Spacing);
        Assert.True(loaded[2].HideLine);

        // A plain separator must serialize exactly as before the spacer feature,
        // so configs written by this version still load in older builds.
        string json = File.ReadAllText(configPath);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(json, "\"spacing\"").Count);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(json, "\"hideLine\"").Count);
        CleanupTempDir();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(7, 2)]
    [InlineData(double.NaN, 0)]
    public void SeparatorSpacingIsClamped(double input, double expected)
    {
        Assert.Equal(expected, DockSeparatorItemModel.SanitizeSpacing(input));
    }

    [Fact]
    public void SupportsSeveralSeparators()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        // Unlike programs, separators are meant to be repeatable.
        var model = new DockModel();
        model.AddItem(new DockSeparatorItemModel());
        model.AddItem(new DockProgramItemModel("Notepad", @"C:\Windows\notepad.exe"));
        model.AddItem(new DockSeparatorItemModel());

        repository.Save(model);
        DockModel loadedModel = repository.Load();

        Assert.Equal(2, loadedModel.Items.OfType<DockSeparatorItemModel>().Count());
        CleanupTempDir();
    }

    [Fact]
    public void SavesAndLoadsSelectedLanguage()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        var model = new DockModel();
        model.Language = SupportedLanguage.PT_BR;

        repository.Save(model);
        DockModel loadedModel = repository.Load();

        Assert.Equal(SupportedLanguage.PT_BR, loadedModel.Language);
        CleanupTempDir();
    }

    [Fact]
    public void SavesAndLoadsDockPosition()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        var model = new DockModel();
        model.SetDockPosition(718.5, 28.2);

        repository.Save(model);
        DockModel loadedModel = repository.Load();

        Assert.Equal(718.5, loadedModel.DockPositionX);
        Assert.Equal(28.2, loadedModel.DockPositionY);
        CleanupTempDir();
    }

    [Fact]
    public void FirstRunFlagsDefaultCreationOnlyOnce()
    {
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath);

        repository.Load();
        Assert.True(repository.WasDefaultCreated);

        var repository2 = new JsonDockRepository(configPath);
        repository2.Load();
        Assert.False(repository2.WasDefaultCreated);
        CleanupTempDir();
    }

    [Fact]
    public void FirstRunPersistsTheSeededItemsAheadOfTheGear()
    {
        // The seed is injected: the production one reads the machine's taskbar
        // pins through COM, which a test must not depend on.
        string configPath = Path.Combine(_tempDir, "config.json");
        var repository = new JsonDockRepository(configPath, () => FirstRunDefaults.Compose(new[]
        {
            new DockProgramItemModel("Notepad", @"C:\Windows\notepad.exe"),
        }));

        repository.Load();

        // Read back through a plain repository: what matters is what landed on disk.
        var reloaded = new JsonDockRepository(configPath).Load();
        Assert.Equal("Notepad", reloaded.Items.OfType<DockProgramItemModel>().Single().Label);
        Assert.Contains(reloaded.Items, i => i is DockSeparatorItemModel);
        Assert.Equal(3, reloaded.Items.OfType<DockWindowsModuleItemModel>().Count());
        Assert.Equal("start", Assert.IsType<DockWindowsModuleItemModel>(reloaded.Items[0]).Module);
        Assert.IsType<DockSettingsItemModel>(reloaded.Items[^1]);
        CleanupTempDir();
    }

    private void CleanupTempDir()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }
}
