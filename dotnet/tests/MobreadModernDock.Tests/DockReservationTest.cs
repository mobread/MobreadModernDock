namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;

/// <summary>
/// Appbar reservation geometry (#4). These pin the rules that keep a bad
/// reservation off the screen: orientation decides the axis, a floating dock
/// reserves nothing, and no strip may swallow more than half the display.
/// </summary>
public class DockReservationTest
{
    private static readonly ScreenBounds Screen = new(0, 0, 1920, 1080);

    // A 600x60 bar resting 8px above the bottom edge - the default look.
    private const double BarW = 600, BarH = 60;
    private static double CentredX => (Screen.Width - BarW) / 2;

    [Fact]
    public void BottomDockIsDetectedAndReservesDownToTheScreenEdge()
    {
        double y = Screen.MaxY - BarH - 8;
        Assert.Equal(ScreenEdge.Bottom, DockReservation.ResolveEdge(CentredX, y, BarW, BarH, Screen));

        var strip = DockReservation.ComputeReservation(CentredX, y, BarW, BarH, Screen);
        Assert.NotNull(strip);
        // Full width, and thick enough to cover the bar *and* its 8px gap.
        Assert.Equal(Screen.MinX, strip!.Value.MinX, 3);
        Assert.Equal(Screen.Width, strip.Value.Width, 3);
        Assert.Equal(BarH + 8, strip.Value.Height, 3);
        Assert.Equal(Screen.MaxY, strip.Value.MaxY, 3);
    }

    [Fact]
    public void TopDockReservesFromTheTopEdgeDown()
    {
        var strip = DockReservation.ComputeReservation(CentredX, Screen.MinY + 8, BarW, BarH, Screen);
        Assert.NotNull(strip);
        Assert.Equal(Screen.MinY, strip!.Value.MinY, 3);
        Assert.Equal(BarH + 8, strip.Value.Height, 3);
        Assert.Equal(Screen.Width, strip.Value.Width, 3);
    }

    [Fact]
    public void VerticalDockOnTheLeftReservesAColumn()
    {
        // 60 wide, 600 tall, 8px from the left.
        double y = (Screen.Height - BarW) / 2;
        Assert.Equal(ScreenEdge.Left, DockReservation.ResolveEdge(8, y, BarH, BarW, Screen));

        var strip = DockReservation.ComputeReservation(8, y, BarH, BarW, Screen);
        Assert.NotNull(strip);
        Assert.Equal(BarH + 8, strip!.Value.Width, 3);
        Assert.Equal(Screen.Height, strip.Value.Height, 3);
        Assert.Equal(Screen.MinY, strip.Value.MinY, 3);
    }

    [Fact]
    public void VerticalDockOnTheRightReservesAColumn()
    {
        double x = Screen.MaxX - BarH - 8;
        double y = (Screen.Height - BarW) / 2;
        Assert.Equal(ScreenEdge.Right, DockReservation.ResolveEdge(x, y, BarH, BarW, Screen));

        var strip = DockReservation.ComputeReservation(x, y, BarH, BarW, Screen);
        Assert.NotNull(strip);
        Assert.Equal(BarH + 8, strip!.Value.Width, 3);
        Assert.Equal(Screen.MaxX, strip.Value.MaxX, 3);
    }

    /// <summary>
    /// The rule that keeps a corner-parked bar from blanking half the screen:
    /// a wide bar in the bottom-left corner touches the left edge too, but it
    /// is a bottom dock, so only a 68px strip is reserved - not a 608px column.
    /// </summary>
    [Fact]
    public void WideBarInACornerReservesTheHorizontalEdgeNotTheVerticalOne()
    {
        double y = Screen.MaxY - BarH;
        Assert.Equal(ScreenEdge.Bottom, DockReservation.ResolveEdge(0, y, BarW, BarH, Screen));

        var strip = DockReservation.ComputeReservation(0, y, BarW, BarH, Screen);
        Assert.Equal(BarH, strip!.Value.Height, 3);
        Assert.Equal(Screen.Width, strip.Value.Width, 3);
    }

    [Fact]
    public void TallBarInACornerReservesTheVerticalEdge()
    {
        Assert.Equal(ScreenEdge.Left, DockReservation.ResolveEdge(0, 0, BarH, BarW, Screen));
        var strip = DockReservation.ComputeReservation(0, 0, BarH, BarW, Screen);
        Assert.Equal(BarH, strip!.Value.Width, 3);
        Assert.Equal(Screen.Height, strip.Value.Height, 3);
    }

    [Fact]
    public void AFloatingDockReservesNothing()
    {
        // Dead centre: far from every edge.
        double x = CentredX, y = (Screen.Height - BarH) / 2;
        Assert.Equal(ScreenEdge.None, DockReservation.ResolveEdge(x, y, BarW, BarH, Screen));
        Assert.Null(DockReservation.ComputeReservation(x, y, BarW, BarH, Screen));
    }

    [Fact]
    public void JustOutsideTheToleranceStopsReserving()
    {
        double inside = Screen.MaxY - BarH - DockReservation.EdgeTolerance;
        double outside = inside - 1;
        Assert.Equal(ScreenEdge.Bottom, DockReservation.ResolveEdge(CentredX, inside, BarW, BarH, Screen));
        Assert.Equal(ScreenEdge.None, DockReservation.ResolveEdge(CentredX, outside, BarW, BarH, Screen));
    }

    [Fact]
    public void ReservationNeverExceedsHalfTheScreen()
    {
        // An absurdly tall "bar" pinned to the bottom would otherwise reserve
        // the whole display and leave no usable work area.
        var strip = DockReservation.ComputeReservation(0, 0, 1920, 1080, Screen);
        Assert.NotNull(strip);
        Assert.True(strip!.Value.Height <= Screen.Height * DockReservation.MaxShare + 1e-6,
            $"reserved {strip.Value.Height}px of {Screen.Height}");
    }

    [Fact]
    public void ADockOnAnotherMonitorReservesNothingHere()
    {
        // Secondary screen to the right; the dock lives on the primary.
        var secondary = new ScreenBounds(1920, 0, 1920, 1080);
        double y = Screen.MaxY - BarH - 8;
        Assert.Equal(ScreenEdge.None, DockReservation.ResolveEdge(CentredX, y, BarW, BarH, secondary));
    }

    [Fact]
    public void ReservationIsComputedAgainstTheDockOwnMonitorIncludingNegativeCoordinates()
    {
        // A monitor to the LEFT of the primary has negative X - the layout
        // that has broken absolute positioning here before.
        var left = new ScreenBounds(-1920, 0, 1920, 1080);
        double x = left.MinX + (left.Width - BarW) / 2;
        double y = left.MaxY - BarH - 8;

        Assert.Equal(ScreenEdge.Bottom, DockReservation.ResolveEdge(x, y, BarW, BarH, left));
        var strip = DockReservation.ComputeReservation(x, y, BarW, BarH, left);
        Assert.Equal(-1920, strip!.Value.MinX, 3);
        Assert.Equal(1920, strip.Value.Width, 3);
    }

    /// <summary>
    /// Regression: snapping must use the FULL monitor while an edge is
    /// reserved, not the work area.
    ///
    /// The work area already excludes the strip this dock reserved, so
    /// snapping to it parks the bar a dock-height above the screen edge. That
    /// gap exceeds <see cref="DockReservation.EdgeTolerance"/>, so the next
    /// pass reads the dock as floating and drops the reservation — and each
    /// snap walks the dock further inward. Snapping against the monitor keeps
    /// the dock on its edge and the reservation stable.
    /// </summary>
    [Fact]
    public void SnappingToTheWorkAreaOfOurOwnReservationWouldUndock()
    {
        var monitor = new ScreenBounds(0, 0, 1920, 1080);
        // We reserve the bottom BarH+8; the work area shrinks to match.
        double reserved = BarH + 8;
        var workArea = new ScreenBounds(0, 0, 1920, 1080 - reserved);

        // Snapping flush to the work area's bottom edge...
        double yFromWorkArea = workArea.MaxY - BarH;
        // ...leaves the bar this far from the real screen edge.
        double gap = monitor.MaxY - (yFromWorkArea + BarH);
        Assert.True(gap > DockReservation.EdgeTolerance,
            $"gap {gap} should exceed the {DockReservation.EdgeTolerance}px tolerance");
        Assert.Equal(ScreenEdge.None,
            DockReservation.ResolveEdge(CentredX, yFromWorkArea, BarW, BarH, monitor));

        // Snapping against the monitor keeps it docked, so the reservation holds.
        double yFromMonitor = monitor.MaxY - BarH;
        Assert.Equal(ScreenEdge.Bottom,
            DockReservation.ResolveEdge(CentredX, yFromMonitor, BarW, BarH, monitor));
    }

    [Fact]
    public void DiffersIgnoresSubPixelNoiseButCatchesRealMoves()
    {
        var a = new ScreenBounds(0, 1020, 1920, 60);
        Assert.False(DockReservation.Differs(a, new ScreenBounds(0, 1020.4, 1920, 60)));
        Assert.True(DockReservation.Differs(a, new ScreenBounds(0, 1000, 1920, 80)));
        Assert.True(DockReservation.Differs(a, null));
        Assert.False(DockReservation.Differs(null, null));
    }
}
