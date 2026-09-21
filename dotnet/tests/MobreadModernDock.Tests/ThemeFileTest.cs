namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

/// <summary>
/// The `.mbtheme` share format. These files arrive from strangers, so most of
/// what matters here is what happens with bad input: parsing must never throw,
/// never half-apply, and never leave the dock in a state the Settings window
/// cannot undo.
/// </summary>
public class ThemeFileTest
{
    private static AppearancePreset Sample() => new()
    {
        Name = "Test theme",
        Author = "somebody",
        IconsSize = 44,
        SpacingBetweenIcons = 6,
        DockTransparency = 0.6,
        DockBorderRounding = 24,
        DockColorRGB = "40, 40, 45, ",
        BlurMode = "acrylic",
        DockPadding = 8,
        MagnifyIcons = true,
        MagnifyScale = 1.8,
    };

    [Fact]
    public void ARoundTripPreservesEveryField()
    {
        var original = Sample();
        var parsed = ThemeFile.TryParse(ThemeFile.Serialize(original));

        Assert.NotNull(parsed);
        Assert.Equal(original.Name, parsed!.Name);
        Assert.Equal(original.Author, parsed.Author);
        Assert.Equal(original.IconsSize, parsed.IconsSize);
        Assert.Equal(original.SpacingBetweenIcons, parsed.SpacingBetweenIcons);
        Assert.Equal(original.DockTransparency, parsed.DockTransparency, 6);
        Assert.Equal(original.DockBorderRounding, parsed.DockBorderRounding);
        Assert.Equal(original.DockColorRGB, parsed.DockColorRGB);
        Assert.Equal(original.BlurMode, parsed.BlurMode);
        Assert.Equal(original.DockPadding, parsed.DockPadding);
        Assert.Equal(original.MagnifyIcons, parsed.MagnifyIcons);
        Assert.Equal(original.MagnifyScale, parsed.MagnifyScale, 6);
    }

    [Fact]
    public void GarbageIsRejectedRatherThanThrowing()
    {
        Assert.Null(ThemeFile.TryParse(""));
        Assert.Null(ThemeFile.TryParse("   "));
        Assert.Null(ThemeFile.TryParse("not json at all"));
        Assert.Null(ThemeFile.TryParse("{ unterminated"));
        Assert.Null(ThemeFile.TryParse("[1,2,3]"));      // JSON, but not an object
        Assert.Null(ThemeFile.TryParse("\"a string\""));
        Assert.Null(ThemeFile.TryParse("null"));
    }

    /// <summary>
    /// The important negative case: an unrelated JSON file deserializes to an
    /// all-default preset without throwing, so applying it would look like the
    /// dock resetting itself. It has to be rejected instead.
    /// </summary>
    [Fact]
    public void AnUnrelatedJsonFileIsNotMistakenForATheme()
    {
        Assert.Null(ThemeFile.TryParse("""{"name":"bob","age":42}"""));
        Assert.Null(ThemeFile.TryParse("""{"dependencies":{"react":"18"}}"""));
        Assert.Null(ThemeFile.TryParse("{}"));
    }

    [Fact]
    public void AThemeFromAFutureSchemaIsRefusedNotHalfApplied()
    {
        string future = $$"""
        {"schemaVersion": {{AppearancePreset.CurrentSchema + 1}}, "iconsSize": 40}
        """;
        Assert.Null(ThemeFile.TryParse(future));
    }

    [Fact]
    public void AThemeWithNoSchemaVersionStillLoads()
    {
        // Hand-written files, and anything exported before the field existed.
        var parsed = ThemeFile.TryParse("""{"name":"hand made","iconsSize":36}""");
        Assert.NotNull(parsed);
        Assert.Equal(36, parsed!.IconsSize);
    }

    [Fact]
    public void OmittedFieldsFallBackToDefaultsInsteadOfZero()
    {
        // A minimal theme must not silently set opacity 0 / rows 0 and make
        // the dock vanish.
        var parsed = ThemeFile.TryParse("""{"dockColorRGB":"10, 20, 30, "}""");
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.DockRows);
        Assert.Equal(1.0, parsed.GlobalOpacity, 6);
        Assert.Equal(10, parsed.DockPadding);
        Assert.Equal("none", parsed.BlurMode);
    }

    [Fact]
    public void OutOfRangeValuesAreClampedIntoWhatTheUiCanUndo()
    {
        string wild = """
        {"iconsSize": 5000, "dockRows": 99, "globalOpacity": 0.0,
         "dockTransparency": 42, "dockBorderRounding": -5,
         "dockPadding": 9999, "magnifyScale": 100, "spacingBetweenIcons": -3}
        """;
        var p = ThemeFile.TryParse(wild);

        Assert.NotNull(p);
        Assert.InRange(p!.IconsSize, 16, 128);
        Assert.InRange(p.DockRows, 1, 4);
        Assert.InRange(p.GlobalOpacity, 0.2, 1.0);   // never invisible
        Assert.InRange(p.DockTransparency, 0.0, 1.0);
        Assert.InRange(p.DockBorderRounding, 0, 60);
        Assert.InRange(p.DockPadding, 0, 40);
        Assert.InRange(p.MagnifyScale, 1.0, 2.5);
        Assert.InRange(p.SpacingBetweenIcons, 0, 40);
    }

    [Fact]
    public void AMalformedColourFallsBackInsteadOfReachingTheBrushParser()
    {
        var p = ThemeFile.TryParse("""
        {"iconsSize": 40, "dockColorRGB": "not,a,colour", "tintColorRGB": "999"}
        """);
        Assert.NotNull(p);
        Assert.Equal("0, 0, 0, ", p!.DockColorRGB);
        Assert.Equal("0, 80, 140", p.TintColorRGB);
    }

    [Fact]
    public void AnUnknownBlurModeBecomesNone()
    {
        var p = ThemeFile.TryParse("""{"iconsSize":40,"blurMode":"rainbow"}""");
        Assert.NotNull(p);
        Assert.Equal("none", p!.BlurMode);
    }

    [Fact]
    public void AnEmptyOrOverlongNameIsMadeUsable()
    {
        var blank = ThemeFile.TryParse("""{"iconsSize":40,"name":"   "}""");
        Assert.False(string.IsNullOrWhiteSpace(blank!.Name));

        var huge = ThemeFile.TryParse($$"""{"iconsSize":40,"name":"{{new string('x', 500)}}"}""");
        Assert.True(huge!.Name.Length <= 60);
    }

    [Fact]
    public void SuggestedFileNameIsShellSafe()
    {
        Assert.Equal("my-theme.mbtheme", ThemeFile.SuggestedFileName("My Theme"));
        var risky = ThemeFile.SuggestedFileName("a/b\\c:d*e?f");
        Assert.DoesNotContain('/', risky);
        Assert.DoesNotContain('\\', risky);
        Assert.DoesNotContain(':', risky);
        Assert.EndsWith(".mbtheme", risky);
        Assert.EndsWith(".mbtheme", ThemeFile.SuggestedFileName(""));
    }

    /// <summary>
    /// Applying a theme is an appearance change only: it must never touch the
    /// user's pinned items, window position or widgets.
    /// </summary>
    [Fact]
    public void ApplyingTouchesAppearanceOnly()
    {
        var dock = new DockModel();
        dock.Items.Add(new DockProgramItemModel { Label = "Keep me", Path = @"C:\app.exe" });
        dock.SetDockPosition(1234, 567);
        dock.Widgets.Add(new WidgetDefinition { Type = "clock" });
        dock.HideTaskbar = true;

        Sample().ApplyTo(dock);

        Assert.Single(dock.Items);
        Assert.Equal(1234, dock.DockPositionX, 6);
        Assert.Equal(567, dock.DockPositionY, 6);
        Assert.Single(dock.Widgets);
        Assert.True(dock.HideTaskbar);
        // ...and the appearance did change.
        Assert.Equal(44, dock.IconsSize);
        Assert.Equal("acrylic", dock.BlurMode);
        Assert.Equal(8, dock.DockPadding);
    }

    /// <summary>
    /// Regression: "Glass" is named after an effect, so it must switch that
    /// effect on. Before blurMode was part of the preset it only set a pale
    /// colour, and the preset appeared to do nothing.
    /// </summary>
    [Fact]
    public void TheGlassBuiltInActuallyEnablesTheGlassEffect()
    {
        var glass = AppearancePreset.BuiltIns().Single(p => p.Name == "Glass");
        Assert.Equal("acrylic", glass.BlurMode);

        var dock = new DockModel();
        glass.ApplyTo(dock);
        Assert.Equal("acrylic", dock.BlurMode);
    }

    [Fact]
    public void CaptureRoundTripsTheLiveModel()
    {
        var dock = new DockModel
        {
            IconsSize = 52, DockPadding = 3, BlurMode = "blur",
            MagnifyIcons = true, MagnifyScale = 2.1, DockRows = 2,
        };

        var captured = AppearancePreset.Capture("snapshot", dock);
        var restored = ThemeFile.TryParse(ThemeFile.Serialize(captured));

        Assert.NotNull(restored);
        var fresh = new DockModel();
        restored!.ApplyTo(fresh);

        Assert.Equal(52, fresh.IconsSize);
        Assert.Equal(3, fresh.DockPadding);
        Assert.Equal("blur", fresh.BlurMode);
        Assert.True(fresh.MagnifyIcons);
        Assert.Equal(2.1, fresh.MagnifyScale, 6);
        Assert.Equal(2, fresh.DockRows);
    }

    /// <summary>
    /// Every theme shipped in docs/themes must parse with the current build.
    /// The gallery is what users download first, so a schema change that
    /// quietly invalidates it would break the feature's front door. Skipped
    /// when the folder is absent (e.g. a source drop without docs).
    /// </summary>
    [Fact]
    public void EveryGalleryThemeParses()
    {
        string? dir = FindGalleryDirectory();
        if (dir == null) return;

        var files = Directory.GetFiles(dir, "*" + ThemeFile.Extension);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var parsed = ThemeFile.TryParse(File.ReadAllText(file));
            Assert.True(parsed != null, $"{Path.GetFileName(file)} is not a valid theme");
            Assert.False(string.IsNullOrWhiteSpace(parsed!.Name),
                $"{Path.GetFileName(file)} has no name");
        }
    }

    /// <summary>Walks up from the test binary to the repo's docs/themes folder.</summary>
    private static string? FindGalleryDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "themes");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
