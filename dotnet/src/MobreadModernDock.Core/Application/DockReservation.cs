namespace MobreadModernDock.Core.Application;

/// <summary>The screen edge a dock occupies, or <see cref="ScreenEdge.None"/> when it floats.</summary>
public enum ScreenEdge
{
    None,
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// Geometry for appbar reservation (#4 "use it as a taskbar"): deciding which
/// screen edge the dock occupies and how much of that edge to reserve, so
/// maximized windows stop at the dock instead of sitting underneath it.
///
/// Pure functions — the Win32 side (<c>AppBarReservation</c>) only translates
/// these results into <c>SHAppBarMessage</c> calls.
/// </summary>
public static class DockReservation
{
    /// <summary>
    /// How close the dock must be to an edge to count as docked there. Wider
    /// than the snap margin (8 px by default) so a nudged dock still reserves,
    /// but far below "floating in the middle of the screen".
    /// </summary>
    public const int EdgeTolerance = 40;

    /// <summary>
    /// An appbar may never take more than this share of the screen. A runaway
    /// reservation is not recoverable from inside the app once the work area
    /// has shrunk, so the clamp is deliberately conservative.
    /// </summary>
    public const double MaxShare = 0.5;

    /// <summary>
    /// Which edge the dock is docked to, or <see cref="ScreenEdge.None"/>.
    ///
    /// Orientation decides the candidate edges before distance does: a wide
    /// horizontal bar pushed into the bottom-left corner is a *bottom* dock,
    /// not a left one, even though it touches both. Reserving the wrong axis
    /// there would blank out half the screen.
    /// </summary>
    public static ScreenEdge ResolveEdge(
        double x, double y, double width, double height,
        ScreenBounds screen, int tolerance = EdgeTolerance)
    {
        if (width <= 0 || height <= 0 || screen.Width <= 0 || screen.Height <= 0)
            return ScreenEdge.None;

        // A dock that does not overlap this screen at all reserves nothing.
        if (x + width <= screen.MinX || x >= screen.MaxX ||
            y + height <= screen.MinY || y >= screen.MaxY)
            return ScreenEdge.None;

        double gapLeft = x - screen.MinX;
        double gapTop = y - screen.MinY;
        double gapRight = screen.MaxX - (x + width);
        double gapBottom = screen.MaxY - (y + height);

        if (width >= height)
        {
            // Horizontal bar: top or bottom only.
            if (gapBottom <= tolerance && gapBottom <= gapTop) return ScreenEdge.Bottom;
            if (gapTop <= tolerance) return ScreenEdge.Top;
            return ScreenEdge.None;
        }

        // Vertical bar: left or right only.
        if (gapRight <= tolerance && gapRight <= gapLeft) return ScreenEdge.Right;
        if (gapLeft <= tolerance) return ScreenEdge.Left;
        return ScreenEdge.None;
    }

    /// <summary>
    /// The strip to hand to <c>ABM_SETPOS</c>: the full span of the chosen
    /// edge, as thick as the dock plus the gap it keeps from the edge (so the
    /// gap stays empty rather than being overlapped by a maximized window).
    /// Returns null when the dock is not docked to an edge.
    /// </summary>
    public static ScreenBounds? ComputeReservation(
        double x, double y, double width, double height,
        ScreenBounds screen, int tolerance = EdgeTolerance)
    {
        var edge = ResolveEdge(x, y, width, height, screen, tolerance);
        return ComputeReservation(edge, x, y, width, height, screen);
    }

    /// <summary>Reservation strip for a known edge. Returns null for <see cref="ScreenEdge.None"/>.</summary>
    public static ScreenBounds? ComputeReservation(
        ScreenEdge edge,
        double x, double y, double width, double height,
        ScreenBounds screen)
    {
        switch (edge)
        {
            case ScreenEdge.Bottom:
            {
                double thickness = Clamp(screen.MaxY - y, screen.Height);
                return new ScreenBounds(screen.MinX, screen.MaxY - thickness, screen.Width, thickness);
            }
            case ScreenEdge.Top:
            {
                double thickness = Clamp(y + height - screen.MinY, screen.Height);
                return new ScreenBounds(screen.MinX, screen.MinY, screen.Width, thickness);
            }
            case ScreenEdge.Right:
            {
                double thickness = Clamp(screen.MaxX - x, screen.Width);
                return new ScreenBounds(screen.MaxX - thickness, screen.MinY, thickness, screen.Height);
            }
            case ScreenEdge.Left:
            {
                double thickness = Clamp(x + width - screen.MinX, screen.Width);
                return new ScreenBounds(screen.MinX, screen.MinY, thickness, screen.Height);
            }
            default:
                return null;
        }
    }

    /// <summary>True when the two strips differ enough to be worth a shell round-trip.</summary>
    public static bool Differs(ScreenBounds? a, ScreenBounds? b, double epsilon = 1.0)
    {
        if (a is null && b is null) return false;
        if (a is null || b is null) return true;
        var (p, q) = (a.Value, b.Value);
        return Math.Abs(p.MinX - q.MinX) > epsilon
            || Math.Abs(p.MinY - q.MinY) > epsilon
            || Math.Abs(p.Width - q.Width) > epsilon
            || Math.Abs(p.Height - q.Height) > epsilon;
    }

    private static double Clamp(double thickness, double screenExtent) =>
        Math.Max(0, Math.Min(thickness, screenExtent * MaxShare));
}
