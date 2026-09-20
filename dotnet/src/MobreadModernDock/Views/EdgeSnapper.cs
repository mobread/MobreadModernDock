using System;
using Avalonia;

namespace MobreadModernDock.Views;

/// <summary>
/// Magnetic snapping for freely dragged windows: when a window's edge or
/// centre line ends up within <see cref="Threshold"/> px of the work area's
/// edge or centre, it is pulled flush to it. Applied once when a drag
/// settles (the OS owns the pointer during BeginMoveDrag).
/// </summary>
public static class EdgeSnapper
{
    public const int Threshold = 24;
    public const int Margin = 8; // distance kept from a snapped screen edge

    /// <summary>Returns the snapped top-left for a window of the given size, or the input when nothing is close.</summary>
    public static PixelPoint Snap(PixelRect window, PixelRect work)
    {
        int x = window.X, y = window.Y;
        int w = window.Width, h = window.Height;

        // Horizontal: left edge, right edge, centre.
        int cx = x + w / 2, workCx = work.X + work.Width / 2;
        if (Math.Abs(x - work.X) <= Threshold) x = work.X + Margin;
        else if (Math.Abs((x + w) - work.Right) <= Threshold) x = work.Right - w - Margin;
        else if (Math.Abs(cx - workCx) <= Threshold) x = workCx - w / 2;

        // Vertical: top edge, bottom edge, centre.
        int cy = y + h / 2, workCy = work.Y + work.Height / 2;
        if (Math.Abs(y - work.Y) <= Threshold) y = work.Y + Margin;
        else if (Math.Abs((y + h) - work.Bottom) <= Threshold) y = work.Bottom - h - Margin;
        else if (Math.Abs(cy - workCy) <= Threshold) y = workCy - h / 2;

        return new PixelPoint(x, y);
    }
}
