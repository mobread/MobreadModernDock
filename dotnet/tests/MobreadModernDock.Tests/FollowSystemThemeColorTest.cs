namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// "Follow Windows light/dark theme" overwrites the dock colour with a
/// preset. The colour the user had picked must survive a round trip through
/// the toggle, or one click throws their choice away.
/// </summary>
public class FollowSystemThemeColorTest
{
    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }

    private const string Purple = "90, 30, 120, ";

    private static (DockAppearanceService Svc, DockModel Model) Build()
    {
        var model = new DockModel();
        var svc = new DockAppearanceService(new DockService(new Repo(model)));
        svc.SetDockColorRGB(Purple);
        return (svc, model);
    }

    [Fact]
    public void TurningFollowThemeOffRestoresThePickedColour()
    {
        var (svc, _) = Build();

        svc.SetFollowSystemTheme(true);
        Assert.True(svc.ApplySystemTheme(isLightTheme: true));
        Assert.Equal(DockAppearanceService.LightThemeColorRGB, svc.GetDockColorRGB());

        bool changed = svc.SetFollowSystemTheme(false);

        Assert.True(changed);
        Assert.Equal(Purple, svc.GetDockColorRGB());
    }

    [Fact]
    public void TheStashSurvivesSeveralThemeSwitches()
    {
        var (svc, _) = Build();
        svc.SetFollowSystemTheme(true);
        svc.ApplySystemTheme(isLightTheme: true);
        svc.ApplySystemTheme(isLightTheme: false);
        svc.ApplySystemTheme(isLightTheme: true);

        svc.SetFollowSystemTheme(false);

        Assert.Equal(Purple, svc.GetDockColorRGB());
    }

    [Fact]
    public void AColourPickedWhileFollowingBecomesTheOneRestored()
    {
        // The user overrides the theme colour by hand while the toggle is on:
        // that, not the older stash, is their latest choice.
        var (svc, _) = Build();
        svc.SetFollowSystemTheme(true);
        svc.ApplySystemTheme(isLightTheme: true);
        const string teal = "0, 120, 110, ";
        svc.SetDockColorRGB(teal);

        svc.SetFollowSystemTheme(false);

        Assert.Equal(teal, svc.GetDockColorRGB());
    }

    [Fact]
    public void AnAppliedPresetWhileFollowingBecomesTheOneRestored()
    {
        var (svc, model) = Build();
        svc.SetFollowSystemTheme(true);
        svc.ApplySystemTheme(isLightTheme: false);
        var preset = new AppearancePreset { Name = "p", DockColorRGB = "10, 25, 60, " };
        preset.ApplyTo(model);

        svc.SetFollowSystemTheme(false);

        Assert.Equal("10, 25, 60, ", svc.GetDockColorRGB());
    }

    [Fact]
    public void TurningFollowThemeOnTwiceDoesNotOverwriteTheStashWithAPreset()
    {
        var (svc, model) = Build();
        svc.SetFollowSystemTheme(true);
        svc.ApplySystemTheme(isLightTheme: true);
        // A redundant "on" (e.g. settings reload) must not stash the light preset.
        svc.SetFollowSystemTheme(true);

        Assert.Equal(Purple, model.CustomDockColorRGB);
    }

    [Fact]
    public void TurningOffWithNothingStashedKeepsTheCurrentColour()
    {
        // A config from before the stash existed: followSystemTheme on, no
        // custom colour recorded. Nothing to restore; nothing changes.
        var model = new DockModel { FollowSystemTheme = true, DockColorRGB = DockAppearanceService.LightThemeColorRGB };
        var svc = new DockAppearanceService(new DockService(new Repo(model)));

        bool changed = svc.SetFollowSystemTheme(false);

        Assert.False(changed);
        Assert.Equal(DockAppearanceService.LightThemeColorRGB, svc.GetDockColorRGB());
    }

    [Fact]
    public void RestoringAnUnchangedColourReportsNoChange()
    {
        // Picked colour equals the theme preset: turning off is a no-op.
        var model = new DockModel();
        var svc = new DockAppearanceService(new DockService(new Repo(model)));
        svc.SetDockColorRGB(DockAppearanceService.DarkThemeColorRGB);
        svc.SetFollowSystemTheme(true);
        svc.ApplySystemTheme(isLightTheme: false);

        Assert.False(svc.SetFollowSystemTheme(false));
    }
}
