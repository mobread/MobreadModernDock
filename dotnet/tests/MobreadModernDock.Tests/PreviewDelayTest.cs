namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// The hover delay before a window preview or tooltip appears. Kept honest
/// because the value is fed straight into a DispatcherTimer interval, where a
/// negative number throws and an unbounded one would look like a hang.
/// </summary>
public class PreviewDelayTest
{
    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }

    private static (DockAppearanceService Svc, DockModel Model) Build()
    {
        var model = new DockModel();
        return (new DockAppearanceService(new DockService(new Repo(model))), model);
    }

    [Fact]
    public void DefaultsToAShortDelay()
    {
        var (svc, _) = Build();
        // Matches the tooltip delay the dock shipped with, so the out-of-box
        // feel is unchanged for users who never touch the slider.
        Assert.Equal(400, svc.GetPreviewDelayMs());
    }

    [Fact]
    public void RoundTripsValuesInRange()
    {
        var (svc, model) = Build();
        foreach (int v in new[] { 0, 250, 1000, DockAppearanceService.MaxPreviewDelayMs })
        {
            svc.SetPreviewDelayMs(v);
            Assert.Equal(v, model.PreviewDelayMs);
            Assert.Equal(v, svc.GetPreviewDelayMs());
        }
    }

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        var (svc, _) = Build();

        svc.SetPreviewDelayMs(99_000);
        Assert.Equal(DockAppearanceService.MaxPreviewDelayMs, svc.GetPreviewDelayMs());

        // A negative interval would throw when handed to the timer.
        svc.SetPreviewDelayMs(-50);
        Assert.Equal(0, svc.GetPreviewDelayMs());
    }

    [Fact]
    public void ZeroMeansImmediate()
    {
        var (svc, _) = Build();
        svc.SetPreviewDelayMs(0);
        // The window treats 0 as "load on pointer-enter", the original behaviour.
        Assert.Equal(0, svc.GetPreviewDelayMs());
    }

    [Fact]
    public void ADelayReadFromAHandEditedConfigIsStillClamped()
    {
        var (svc, model) = Build();
        model.PreviewDelayMs = 60_000;          // someone edited config.json
        Assert.Equal(DockAppearanceService.MaxPreviewDelayMs, svc.GetPreviewDelayMs());

        model.PreviewDelayMs = -1;
        Assert.Equal(0, svc.GetPreviewDelayMs());
    }

    [Fact]
    public void SurvivesConfigImport()
    {
        var target = new DockModel();
        var imported = new DockModel { PreviewDelayMs = 1234 };
        imported.Items.Add(new DockSettingsItemModel());

        target.CopyFrom(imported);

        Assert.Equal(1234, target.PreviewDelayMs);
    }
}
