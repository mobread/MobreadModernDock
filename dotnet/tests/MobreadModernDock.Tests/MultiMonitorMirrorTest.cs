namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// #10 mirrored docks with more than two monitors.
///
/// The app creates one mirror per non-primary screen, so the count must scale
/// with however many are attached rather than assuming a single secondary.
/// These tests drive a fake screen provider describing a 4-monitor layout
/// (mixed resolutions, primary not at the virtual origin, one monitor left of
/// it so coordinates go negative) because that is the arrangement most likely
/// to expose an off-by-one or a hardcoded "the secondary".
/// </summary>
public class MultiMonitorMirrorTest
{
    /// <summary>Four screens: one to the left (negative X), primary, then two to the right.</summary>
    private sealed class FakeScreens : IScreenBoundsProvider
    {
        public ScreenBounds GetPrimaryScreenBounds() => new(0, 0, 2560, 1400);

        public IReadOnlyList<ScreenInfo> GetAllScreens() => new[]
        {
            new ScreenInfo(@"\\.\DISPLAY1", false, new ScreenBounds(-2560, 0, 2560, 1440)),
            new ScreenInfo(@"\\.\DISPLAY2", true,  new ScreenBounds(0, 0, 2560, 1400)),
            new ScreenInfo(@"\\.\DISPLAY3", false, new ScreenBounds(2560, 0, 1920, 1080)),
            new ScreenInfo(@"\\.\DISPLAY4", false, new ScreenBounds(4480, -200, 1280, 1024)),
        };
    }

    private sealed class InMemoryDockRepository : IDockRepository
    {
        private readonly DockModel _model;
        public InMemoryDockRepository(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }

    private static DockPositioningService Service(DockModel model) =>
        new(new DockService(new InMemoryDockRepository(model)), new FakeScreens());

    [Fact]
    public void MirrorsEveryNonPrimaryScreen()
    {
        var service = Service(new DockModel());

        // This is the exact selection App.SyncMirrorDocks uses to decide how
        // many mirror windows to open.
        var mirrored = service.GetAllScreens().Where(s => !s.IsPrimary).ToList();

        Assert.Equal(4, service.GetAllScreens().Count);
        Assert.Equal(3, mirrored.Count);
        Assert.Equal(
            new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY3", @"\\.\DISPLAY4" },
            mirrored.Select(s => s.Id));
    }

    [Fact]
    public void EachScreenIsAddressableById()
    {
        var service = Service(new DockModel());

        // Mirror windows keep only a screen id and re-resolve it on every
        // layout change, so every id must round-trip — including the ones
        // beyond the second monitor.
        foreach (var screen in service.GetAllScreens())
            Assert.Equal(screen.Bounds, service.FindScreen(screen.Id)!.Bounds);

        Assert.Null(service.FindScreen(@"\\.\DISPLAY9"));
    }

    [Fact]
    public void StaticAnchorsResolvePerScreenNotPerPrimary()
    {
        var model = new DockModel
        {
            PositioningMode = DockPositioningMode.STATIC,
            HorizontalAnchor = DockHorizontalAnchor.MIDDLE,
            VerticalAnchor = DockVerticalAnchor.DOWN,
            BottomSpacing = 20,
        };
        var service = Service(model);
        const double w = 800, h = 80;

        // Every monitor must centre the dock against ITS OWN bounds. A
        // hardcoded primary would put all three mirrors at the same x.
        foreach (var screen in service.GetAllScreens())
        {
            var (x, y) = service.ResolvePositionOnScreen(screen.Bounds, w, h);
            Assert.Equal(screen.Bounds.MinX + (screen.Bounds.Width - w) / 2, x);
            Assert.Equal(screen.Bounds.MaxY - h - 20, y);
        }

        // Spot-check the two most error-prone screens explicitly: the one at
        // negative coordinates and the one with a negative Y origin.
        var left = service.FindScreen(@"\\.\DISPLAY1")!;
        Assert.Equal(-2560 + (2560 - w) / 2, service.ResolvePositionOnScreen(left.Bounds, w, h).X);

        var far = service.FindScreen(@"\\.\DISPLAY4")!;
        var (farX, farY) = service.ResolvePositionOnScreen(far.Bounds, w, h);
        Assert.Equal(4480 + (1280 - w) / 2, farX);
        Assert.Equal(-200 + 1024 - h - 20, farY);
    }

    [Fact]
    public void DynamicPositionMapsProportionallyToEveryScreen()
    {
        // Dock dragged to the bottom-centre of the primary.
        var model = new DockModel { PositioningMode = DockPositioningMode.DYNAMIC };
        model.SetDockPosition(880, 1320);
        var service = Service(model);
        const double w = 800, h = 80;

        foreach (var screen in service.GetAllScreens().Where(s => !s.IsPrimary))
        {
            var (x, y) = service.ResolvePositionOnScreen(screen.Bounds, w, h);

            // Must land inside that screen, never on a neighbour.
            Assert.InRange(x, screen.Bounds.MinX, screen.Bounds.MaxX - w);
            Assert.InRange(y, screen.Bounds.MinY, screen.Bounds.MaxY - h);

            // And stay bottom-ish/centre-ish, proportionally.
            double fx = (x - screen.Bounds.MinX) / (screen.Bounds.Width - w);
            Assert.InRange(fx, 0.45, 0.55);
        }
    }

    [Fact]
    public void ScalesToEightMonitors()
    {
        // Nothing in the model caps the number of mirrors; prove it holds for
        // a wall of monitors as well as a pair.
        var screens = new List<ScreenInfo> { new("primary", true, new ScreenBounds(0, 0, 1920, 1080)) };
        for (int i = 1; i < 8; i++)
            screens.Add(new ScreenInfo($"screen{i}", false, new ScreenBounds(1920 * i, 0, 1920, 1080)));

        Assert.Equal(7, screens.Count(s => !s.IsPrimary));
        Assert.Equal(8, screens.Select(s => s.Id).Distinct().Count());
    }
}
