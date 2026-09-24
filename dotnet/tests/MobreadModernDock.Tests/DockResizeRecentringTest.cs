namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;

/// <summary>
/// A freely-dragged (DYNAMIC) dock whose bar changes size - icon size,
/// padding, items, running apps - must not drift. The window is top-left
/// anchored, so without this a centred dock drifts right and a bottom dock
/// grows down past the screen edge. Rule per axis: centred stays centred,
/// flush with the far edge stays flush, otherwise the start edge is kept.
/// </summary>
public class DockResizeRecentringTest
{
    private static readonly ScreenBounds Screen = new(0, 0, 2560, 1440);

    [Fact]
    public void ACentredBottomDockStaysCentredAndFlushWhenItGrows()
    {
        // 1000x80 bar centred and flush with the bottom; icons get bigger.
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 780, oldY: 1360, oldWidth: 1000, oldHeight: 80,
            newWidth: 1400, newHeight: 110);

        Assert.Equal((2560 - 1400) / 2, x, 3);
        Assert.Equal(1440 - 110, y, 3);   // grew upward, still flush
    }

    [Fact]
    public void ACentredDockStaysCentredWhenItShrinks()
    {
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 580, oldY: 1330, oldWidth: 1400, oldHeight: 110,
            newWidth: 1000, newHeight: 80);

        Assert.Equal((2560 - 1000) / 2, x, 3);
        Assert.Equal(1440 - 80, y, 3);
    }

    [Fact]
    public void AFewPixelsOffCentreStillCountsAsCentred()
    {
        // Odd widths round the centre by a pixel; running-app churn can add a
        // few more. Those must not turn a centred dock into a left-anchored one.
        var (x, _) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 785, oldY: 1360, oldWidth: 1000, oldHeight: 80,
            newWidth: 1400, newHeight: 80);

        Assert.Equal((2560 - 1400) / 2, x, 3);
    }

    [Fact]
    public void AnOffCentreDockKeepsItsStartEdge()
    {
        // Deliberately parked left of centre: grows to the right, as before.
        var (x, _) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 200, oldY: 1360, oldWidth: 600, oldHeight: 80,
            newWidth: 800, newHeight: 80);

        Assert.Equal(200, x, 3);
    }

    [Fact]
    public void ADockFlushWithTheRightEdgeStaysFlush()
    {
        var (x, _) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 1960, oldY: 1360, oldWidth: 600, oldHeight: 80,
            newWidth: 800, newHeight: 80);

        Assert.Equal(2560 - 800, x, 3);
    }

    [Fact]
    public void ADockSnappedAFewPixelsInFromTheRightKeepsItsGap()
    {
        // The edge snapper parks the bar EdgeSnapMargin (3px) from the edge.
        // Growing must keep that gap; closing it only for the snapper to
        // reopen it made the dock jump on every magnification step.
        var (x, _) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 2483, oldY: 116, oldWidth: 74, oldHeight: 1200,
            newWidth: 119, newHeight: 1200);

        Assert.Equal(2560 - 3 - 119, x, 3);
    }

    [Fact]
    public void ADockFlushWithTheLeftEdgeStaysOnScreen()
    {
        var (x, _) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 0, oldY: 1360, oldWidth: 600, oldHeight: 80,
            newWidth: 800, newHeight: 80);

        Assert.Equal(0, x, 3);
    }

    [Fact]
    public void AVerticalDockOnTheLeftEdgeStaysFlushAndCentredTopToBottom()
    {
        // 80x1000 bar on the left, centred vertically; grows to 110x1400.
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 0, oldY: 220, oldWidth: 80, oldHeight: 1000,
            newWidth: 110, newHeight: 1400);

        Assert.Equal(0, x, 3);
        Assert.Equal((1440 - 1400) / 2, y, 3);
    }

    [Fact]
    public void WorksOnAMonitorWithNegativeCoordinates()
    {
        var left = new ScreenBounds(-2560, 0, 2560, 1440);
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            left, oldX: -1780, oldY: 1360, oldWidth: 1000, oldHeight: 80,
            newWidth: 1400, newHeight: 110);

        Assert.Equal(-2560 + (2560 - 1400) / 2, x, 3);
        Assert.Equal(1440 - 110, y, 3);
    }

    [Fact]
    public void AnUnchangedSizeIsANoOp()
    {
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 300, oldY: 1300, oldWidth: 1000, oldHeight: 80,
            newWidth: 1000, newHeight: 80);

        Assert.Equal(300, x, 3);
        Assert.Equal(1300, y, 3);
    }

    [Fact]
    public void RepeatedResizesDoNotCreep()
    {
        // Bounce between two sizes many times; a centred dock must land on
        // exactly the same pixel each time round.
        double x = 780, y = 1360;
        for (int i = 0; i < 50; i++)
        {
            (x, y) = DockPositioningService.KeepPlacementAfterResize(Screen, x, y, 1000, 80, 1321, 86);
            (x, y) = DockPositioningService.KeepPlacementAfterResize(Screen, x, y, 1321, 86, 1000, 80);
        }
        Assert.Equal(780, x, 3);
        Assert.Equal(1360, y, 3);
    }

    [Fact]
    public void AStartupPassWithNoPreviousSizeLeavesThePersistedPositionAlone()
    {
        // First layout: previous size is zero. A zero-size "bar" at the
        // persisted point is neither centred nor flush, so nothing moves.
        var (x, y) = DockPositioningService.KeepPlacementAfterResize(
            Screen, oldX: 600, oldY: 1354, oldWidth: 0, oldHeight: 0,
            newWidth: 1300, newHeight: 86);

        Assert.Equal(600, x, 3);
        Assert.Equal(1354, y, 3);
    }
}
