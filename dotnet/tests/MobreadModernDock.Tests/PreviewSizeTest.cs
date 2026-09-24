namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// The "Preview size" setting and the geometry that turns it into a
/// thumbnail size that still fits between the dock and the screen edge.
/// </summary>
public class PreviewSizeTest
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

    private const double Inf = double.PositiveInfinity;

    [Fact]
    public void DefaultsToTheStockSize()
    {
        var (svc, _) = Build();
        Assert.Equal(100, svc.GetPreviewSizePercent());
        Assert.Equal((144d, 81d), PreviewThumbnailSizing.Fit(100, 1, Inf, Inf));
    }

    [Fact]
    public void ClampsSettingAndHandEditedConfig()
    {
        var (svc, model) = Build();
        svc.SetPreviewSizePercent(5000);
        Assert.Equal(DockAppearanceService.MaxPreviewSizePercent, svc.GetPreviewSizePercent());
        model.PreviewSizePercent = 1;
        Assert.Equal(DockAppearanceService.MinPreviewSizePercent, svc.GetPreviewSizePercent());
    }

    [Fact]
    public void ScalesKeepingAspect()
    {
        var (w, h) = PreviewThumbnailSizing.Fit(200, 1, Inf, Inf);
        Assert.Equal(162, h);
        Assert.Equal(288, w);
    }

    [Fact]
    public void ShrinksSoAllRowsFitAboveTheDock()
    {
        // 300% = 243px tall; four rows of that never fit in 700px.
        var (_, h) = PreviewThumbnailSizing.Fit(300, 4, Inf, 700);
        double total = PreviewThumbnailSizing.PanelChrome
                       + 4 * (h + PreviewThumbnailSizing.RowVerticalChrome)
                       + 3 * PreviewThumbnailSizing.RowSpacing;
        Assert.True(total <= 700, $"stack is {total}px");
        Assert.True(h < 243);
    }

    [Fact]
    public void ShrinksToTheWidthBesideAVerticalDock()
    {
        var (w, _) = PreviewThumbnailSizing.Fit(300, 1, 300, Inf);
        Assert.True(w + PreviewThumbnailSizing.RowHorizontalChrome + PreviewThumbnailSizing.PanelChrome <= 301);
    }

    [Fact]
    public void NeverCollapsesBelowTheFloor()
    {
        var (_, h) = PreviewThumbnailSizing.Fit(100, 40, Inf, 200);
        Assert.Equal(PreviewThumbnailSizing.MinHeight, h);
    }

    [Fact]
    public void SurvivesConfigImport()
    {
        var target = new DockModel();
        var imported = new DockModel { PreviewSizePercent = 180 };
        imported.Items.Add(new DockSettingsItemModel());
        target.CopyFrom(imported);
        Assert.Equal(180, target.PreviewSizePercent);
    }
}
