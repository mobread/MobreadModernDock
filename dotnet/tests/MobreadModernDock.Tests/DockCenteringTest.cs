namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;

/// <summary>
/// "Centre dock" from the gear's context menu. The rule that matters is that
/// centring moves the dock along the edge it sits on and never off that edge.
/// </summary>
public class DockCenteringTest
{
    private static readonly ScreenBounds Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void AHorizontalDockIsCentredLeftToRightAndKeepsItsY()
    {
        // 600x60 bar shoved to the left, sitting near the bottom edge.
        var (x, y) = DockPositioningService.CenterAlongEdge(
            Screen, x: 40, y: 1012, windowWidth: 600, windowHeight: 60, verticalDock: false);

        Assert.Equal((1920 - 600) / 2, x, 3);
        Assert.Equal(1012, y, 3);   // stays on its edge
    }

    [Fact]
    public void AVerticalDockIsCentredTopToBottomAndKeepsItsX()
    {
        // 60x600 bar on the left edge, shoved toward the top. Centring it
        // horizontally would fling it into the middle of the screen.
        var (x, y) = DockPositioningService.CenterAlongEdge(
            Screen, x: 8, y: 40, windowWidth: 60, windowHeight: 600, verticalDock: true);

        Assert.Equal(8, x, 3);      // stays on its edge
        Assert.Equal((1080 - 600) / 2, y, 3);
    }

    [Fact]
    public void CentringIsIdempotent()
    {
        var first = DockPositioningService.CenterAlongEdge(
            Screen, 40, 1012, 600, 60, verticalDock: false);
        var second = DockPositioningService.CenterAlongEdge(
            Screen, first.X, first.Y, 600, 60, verticalDock: false);

        Assert.Equal(first.X, second.X, 6);
        Assert.Equal(first.Y, second.Y, 6);
    }

    /// <summary>
    /// A dock dragged onto a monitor left of the primary has negative
    /// coordinates. Centring must use that monitor's own bounds, not the
    /// primary's, or the dock jumps screens - the bug class that has hit
    /// positioning here before.
    /// </summary>
    [Fact]
    public void CentringOnAMonitorWithNegativeCoordinatesStaysOnThatMonitor()
    {
        var left = new ScreenBounds(-1920, 0, 1920, 1080);
        var (x, y) = DockPositioningService.CenterAlongEdge(
            left, x: -1800, y: 1012, windowWidth: 600, windowHeight: 60, verticalDock: false);

        Assert.Equal(-1920 + (1920 - 600) / 2, x, 3);
        Assert.True(x < 0, "dock left its monitor");
        Assert.Equal(1012, y, 3);
    }

    [Fact]
    public void CentringUsesTheWorkAreaOriginNotZero()
    {
        // A work area inset by a taskbar on the left: centring must be
        // relative to the usable region, not the raw monitor.
        var work = new ScreenBounds(80, 0, 1840, 1080);
        var (x, _) = DockPositioningService.CenterAlongEdge(
            work, x: 100, y: 1012, windowWidth: 600, windowHeight: 60, verticalDock: false);

        Assert.Equal(80 + (1840 - 600) / 2, x, 3);
    }

    [Fact]
    public void ADockWiderThanTheScreenIsPlacedAtTheEdgeNotOffIt()
    {
        // Degenerate but reachable with many icons on a small screen: the
        // result is negative-symmetric, i.e. equally overhanging both sides,
        // rather than shunted entirely off one end.
        var (x, _) = DockPositioningService.CenterAlongEdge(
            Screen, x: 0, y: 1012, windowWidth: 2400, windowHeight: 60, verticalDock: false);

        Assert.Equal((1920 - 2400) / 2, x, 3);
        Assert.Equal(1920 - 2400 - x, x, 3); // same overhang on both sides
    }

    [Fact]
    public void ResultsAreWholePixels()
    {
        // An odd leftover width would otherwise land the dock on a half pixel
        // and render the icons blurry.
        var (x, y) = DockPositioningService.CenterAlongEdge(
            Screen, x: 33, y: 1011, windowWidth: 601, windowHeight: 61, verticalDock: false);

        Assert.Equal(Math.Round(x), x, 9);
        Assert.Equal(Math.Round(y), y, 9);
    }

    /// <summary>
    /// Regression: centring must never be applied to a mirror dock.
    ///
    /// A mirror derives its position from the primary's saved position as a
    /// fraction of the primary's screen. If a mirror on a monitor LEFT of the
    /// primary centred itself, it would persist a negative X as the primary's
    /// position; the fraction then clamps to 0 and every mirror slams against
    /// its left edge. This reproduces that arithmetic so the invariant is
    /// pinned even though the guard itself lives in the view.
    /// </summary>
    [Fact]
    public void AMirrorsCentredCoordinateWouldCollapseTheFractionToZero()
    {
        var primary = new ScreenBounds(0, 0, 2560, 1440);
        var leftMonitor = new ScreenBounds(-2560, 0, 2560, 1440);
        const double dockW = 928;

        // What the mirror would compute for itself.
        var (mirrorX, _) = DockPositioningService.CenterAlongEdge(
            leftMonitor, -1800, 1356, dockW, 80, verticalDock: false);
        Assert.True(mirrorX < 0, "precondition: the mirror's centre is negative");

        // Persisting that as the PRIMARY position and re-deriving the mirror
        // is the bug: the fraction clamps to 0 => hard left.
        double freeW = Math.Max(1, primary.Width - dockW);
        double fx = Math.Clamp((mirrorX - primary.MinX) / freeW, 0, 1);
        Assert.Equal(0, fx, 9);

        double derived = leftMonitor.MinX + fx * Math.Max(0, leftMonitor.Width - dockW);
        Assert.Equal(leftMonitor.MinX, derived, 6);   // slammed to the left edge

        // Centring the PRIMARY instead keeps every mirror centred.
        var (primaryX, _) = DockPositioningService.CenterAlongEdge(
            primary, 1030, 1356, dockW, 80, verticalDock: false);
        double fxOk = Math.Clamp((primaryX - primary.MinX) / freeW, 0, 1);
        double derivedOk = leftMonitor.MinX + fxOk * Math.Max(0, leftMonitor.Width - dockW);
        Assert.Equal(leftMonitor.MinX + (leftMonitor.Width - dockW) / 2, derivedOk, 3);
    }
}
