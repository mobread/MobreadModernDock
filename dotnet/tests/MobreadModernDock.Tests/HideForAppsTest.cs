namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// Per-app hide rules are matched against the foreground process every poll
/// tick, so the comparison must be by bare file name, case-insensitive, and
/// must tolerate quoted / full paths from every entry point.
/// </summary>
public class HideForAppsTest
{
    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public int Saves;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) => Saves++;
    }

    private static (DockAppearanceService Svc, DockModel Model, Repo Repo) Build()
    {
        var model = new DockModel();
        var repo = new Repo(model);
        return (new DockAppearanceService(new DockService(repo)), model, repo);
    }

    [Theory]
    [InlineData(@"C:\Games\Foo\Game.exe", "game.exe")]
    [InlineData("\"C:\\Program Files\\Bar\\bar.EXE\"", "bar.exe")]
    [InlineData("  notepad.exe  ", "notepad.exe")]
    [InlineData("", "")]
    public void NormalizesToLowerCaseFileName(string input, string expected)
    {
        Assert.Equal(expected, DockAppearanceService.NormalizeExeName(input));
    }

    [Fact]
    public void AddIsDeduplicatedCaseInsensitively()
    {
        var (svc, model, repo) = Build();
        Assert.True(svc.AddHideForApp(@"C:\a\Game.exe"));
        Assert.False(svc.AddHideForApp("GAME.EXE"));
        Assert.Single(model.HideForApps);
        Assert.Equal("game.exe", model.HideForApps[0]);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public void MatchesForegroundByFileName()
    {
        var (svc, _, _) = Build();
        svc.AddHideForApp("game.exe");
        Assert.True(svc.IsHideForApp(@"D:\Other\Path\GAME.exe"));
        Assert.False(svc.IsHideForApp(@"D:\Other\Path\game2.exe"));
        Assert.False(svc.IsHideForApp(null));
        Assert.False(svc.IsHideForApp(""));
    }

    [Fact]
    public void RemoveAcceptsPathOrName()
    {
        var (svc, model, repo) = Build();
        svc.AddHideForApp("game.exe");
        svc.RemoveHideForApp(@"C:\x\Game.EXE");
        Assert.Empty(model.HideForApps);
        Assert.Equal(2, repo.Saves);
        // Removing something absent does not touch the disk.
        svc.RemoveHideForApp("nothing.exe");
        Assert.Equal(2, repo.Saves);
    }

    [Fact]
    public void LockDockDefaultsOffAndPersists()
    {
        var (svc, model, repo) = Build();
        Assert.False(svc.GetLockDock());
        svc.SetLockDock(true);
        Assert.True(model.LockDock);
        Assert.Equal(1, repo.Saves);
    }
}
