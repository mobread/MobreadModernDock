namespace MobreadModernDock.Tests;

using System.Text.Json;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>Dock-wide and per-separator line colours.</summary>
public class SeparatorColorTest
{
    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }

    [Fact]
    public void DefaultReproducesTheOriginalLook()
    {
        // White at 35 %: what the hard-coded line was before it was configurable.
        var (a, r, g, b) = SeparatorColors.ToArgb(SeparatorColors.Default);
        Assert.Equal((byte)0x59, a);
        Assert.Equal((255, 255, 255), (r, g, b));
        Assert.Equal(SeparatorColors.Default, new DockModel().SeparatorColor);
    }

    [Theory]
    [InlineData("#ff0000", "#FFFF0000")]
    [InlineData("80ff0000", "#80FF0000")]
    [InlineData(" #CC3B82F6 ", "#CC3B82F6")]
    [InlineData("red", null)]
    [InlineData("#12345", null)]
    [InlineData("", null)]
    public void NormalizesHex(string input, string? expected) =>
        Assert.Equal(expected, SeparatorColors.Normalize(input));

    [Fact]
    public void InvalidDockWideColourFallsBackToDefault()
    {
        var model = new DockModel { SeparatorColor = "not a colour" };
        var svc = new DockAppearanceService(new DockService(new Repo(model)));
        Assert.Equal(SeparatorColors.Default, svc.GetSeparatorColor());
    }

    [Fact]
    public void PerSeparatorColourIsSetAndCleared()
    {
        var model = new DockModel();
        model.Items.Add(new DockSeparatorItemModel());
        model.Items.Add(new DockSettingsItemModel());
        var dock = new DockService(new Repo(model));

        dock.SetSeparatorColor(0, "#cc22c55e");
        Assert.Equal("#CC22C55E", ((DockSeparatorItemModel)model.Items[0]).Color);

        dock.SetSeparatorColor(0, null);
        Assert.Null(((DockSeparatorItemModel)model.Items[0]).Color);
    }

    [Fact]
    public void UncolouredSeparatorOmitsTheKey()
    {
        // Older configs must round-trip byte-for-byte: no "color": null noise.
        string json = JsonSerializer.Serialize(new DockSeparatorItemModel());
        Assert.DoesNotContain("color", json);
    }

    [Fact]
    public void ThemeCarriesTheColourAndOlderThemesLeaveItAlone()
    {
        var source = new DockModel { SeparatorColor = "#CCEF4444" };
        var preset = AppearancePreset.Capture("Red lines", source);
        Assert.Equal("#CCEF4444", preset.SeparatorColor);

        var target = new DockModel { SeparatorColor = "#CC3B82F6" };
        new AppearancePreset { Name = "old theme" }.ApplyTo(target); // no separatorColor key
        Assert.Equal("#CC3B82F6", target.SeparatorColor);

        preset.ApplyTo(target);
        Assert.Equal("#CCEF4444", target.SeparatorColor);
    }

    [Fact]
    public void SanitizeDropsAMalformedThemeColour()
    {
        var preset = new AppearancePreset { Name = "x", SeparatorColor = "#zzzzzz" };
        preset.Sanitize();
        Assert.Null(preset.SeparatorColor);
    }

    [Fact]
    public void SurvivesConfigImport()
    {
        var target = new DockModel();
        var imported = new DockModel { SeparatorColor = "#CCA855F7" };
        imported.Items.Add(new DockSettingsItemModel());
        target.CopyFrom(imported);
        Assert.Equal("#CCA855F7", target.SeparatorColor);
    }
}
