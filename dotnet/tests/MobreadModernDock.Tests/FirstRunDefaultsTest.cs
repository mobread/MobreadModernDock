namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

/// <summary>
/// First-run seeding. A fresh install used to show a dock holding nothing but
/// the Settings gear, which reads as broken; these pin down what it shows now.
/// </summary>
public class FirstRunDefaultsTest
{
    private static DockProgramItemModel Program(string label, string exe) => new(label, exe);

    [Fact]
    public void SeedsStartMenuThenTaskbarPinsBetweenDividersThenModules()
    {
        var items = FirstRunDefaults.Compose(new[]
        {
            Program("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe"),
            Program("Notepad", @"C:\Windows\notepad.exe"),
        });

        Assert.Equal(2, items.OfType<DockProgramItemModel>().Count());
        // Order matters: Start furthest left, a divider, programs, a divider,
        // then the remaining Windows modules.
        var start = Assert.IsType<DockWindowsModuleItemModel>(items[0]);
        Assert.Equal("start", start.Module);
        Assert.IsType<DockSeparatorItemModel>(items[1]);
        Assert.IsType<DockProgramItemModel>(items[2]);
        Assert.IsType<DockProgramItemModel>(items[3]);
        Assert.IsType<DockSeparatorItemModel>(items[4]);
        Assert.Equal(new[] { "start", "mypc", "trash" },
            items.OfType<DockWindowsModuleItemModel>().Select(m => m.Module));
    }

    [Fact]
    public void StartMenuIsAlwaysTheFirstItem()
    {
        Assert.Equal("start", Assert.IsType<DockWindowsModuleItemModel>(FirstRunDefaults.Compose(null)[0]).Module);
        Assert.Equal("start", Assert.IsType<DockWindowsModuleItemModel>(FirstRunDefaults.Compose(
            new[] { Program("Notepad", @"C:\Windows\notepad.exe") })[0]).Module);
    }

    [Fact]
    public void OmitsTheDividersWhenThereAreNoPrograms()
    {
        // Two adjacent dividers (or one against the edge) look like a glitch.
        var items = FirstRunDefaults.Compose(Array.Empty<DockProgramItemModel>());

        Assert.DoesNotContain(items, i => i is DockSeparatorItemModel);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void FallsBackWhenThereAreNoTaskbarPins()
    {
        var items = FirstRunDefaults.Compose(
            Array.Empty<DockProgramItemModel>(),
            new[] { Program("Notepad", @"C:\Windows\notepad.exe") });

        var program = Assert.Single(items.OfType<DockProgramItemModel>());
        Assert.Equal(@"C:\Windows\notepad.exe", program.ExecutablePath);
    }

    [Fact]
    public void PrefersPinsOverTheFallback()
    {
        var items = FirstRunDefaults.Compose(
            new[] { Program("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe") },
            new[] { Program("Notepad", @"C:\Windows\notepad.exe") });

        var program = Assert.Single(items.OfType<DockProgramItemModel>());
        Assert.Equal("Firefox", program.Label);
    }

    [Fact]
    public void CapsTheNumberOfSeededPrograms()
    {
        // 30 pins would make the dock wider than the screen on first launch.
        var many = Enumerable.Range(0, 30)
            .Select(i => Program($"App{i}", $@"C:\Apps\app{i}.exe"))
            .ToArray();

        var items = FirstRunDefaults.Compose(many);

        Assert.Equal(FirstRunDefaults.MaxSeededPrograms, items.OfType<DockProgramItemModel>().Count());
    }

    [Fact]
    public void DropsDuplicateTargetsAndTheDockItself()
    {
        var items = FirstRunDefaults.Compose(new[]
        {
            Program("Notepad", @"C:\Windows\notepad.exe"),
            Program("Notepad (2)", @"C:\WINDOWS\NOTEPAD.EXE"),
            Program("Mobread Modern Dock", @"C:\Program Files\Mobread\MobreadModernDock.exe"),
        });

        var program = Assert.Single(items.OfType<DockProgramItemModel>());
        Assert.Equal(@"C:\Windows\notepad.exe", program.ExecutablePath);
    }

    [Fact]
    public void TolerantOfNullInput()
    {
        var items = FirstRunDefaults.Compose(null);

        Assert.Equal(3, items.OfType<DockWindowsModuleItemModel>().Count());
    }

    [Fact]
    public void LoadDefaultItemsKeepsTheGearLastAfterTheSeed()
    {
        var model = new DockModel();
        model.LoadDefaultItems(FirstRunDefaults.Compose(new[]
        {
            Program("Notepad", @"C:\Windows\notepad.exe"),
        }));

        Assert.IsType<DockSettingsItemModel>(model.Items[^1]);
        Assert.Single(model.Items.OfType<DockSettingsItemModel>());
    }

    [Fact]
    public void LoadDefaultItemsWithoutASeedIsGearOnly()
    {
        var model = new DockModel();
        model.LoadDefaultItems();

        Assert.Single(model.Items);
        Assert.IsType<DockSettingsItemModel>(model.Items[0]);
    }

    [Fact]
    public void SeededModulesCarryTheirIconAndLabel()
    {
        var items = FirstRunDefaults.Compose(null);

        var recycleBin = items.OfType<DockWindowsModuleItemModel>().Single(m => m.Module == "trash");
        Assert.Equal("Recycle Bin", recycleBin.Label);
        Assert.EndsWith("trash.png", recycleBin.Path);
        var start = items.OfType<DockWindowsModuleItemModel>().Single(m => m.Module == "start");
        Assert.Equal("Start Menu", start.Label);
        Assert.EndsWith("start_menu.png", start.Path);
    }
}
