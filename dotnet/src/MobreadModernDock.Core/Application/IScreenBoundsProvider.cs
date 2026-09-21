namespace MobreadModernDock.Core.Application;

/// <summary>
/// Port providing screen geometry, abstracting the platform-specific screen
/// enumeration (Avalonia Screen / Win32 monitor enumeration).
/// Coordinates are in device pixels.
/// </summary>
public interface IScreenBoundsProvider
{
    /// <summary>The visual bounds of the primary screen (work area, excluding taskbar).</summary>
    ScreenBounds GetPrimaryScreenBounds();

    /// <summary>
    /// The primary monitor's full bounds, taskbar and appbars included.
    ///
    /// Anything that must not be influenced by this app's own screen-edge
    /// reservation has to measure against this rather than the work area —
    /// the work area already excludes our reserved strip, so using it in a
    /// calculation that feeds back into the dock's position makes the dock
    /// creep on every pass. Defaults to the work area for implementations
    /// that cannot tell the two apart.
    /// </summary>
    ScreenBounds GetPrimaryMonitorBounds() => GetPrimaryScreenBounds();

    /// <summary>All attached screens. Default: just the primary.</summary>
    IReadOnlyList<ScreenInfo> GetAllScreens() => new[] { new ScreenInfo("primary", true, GetPrimaryScreenBounds()) };
}

/// <summary>A monitor: stable OS identifier, primary flag, work-area bounds.</summary>
public sealed record ScreenInfo(string Id, bool IsPrimary, ScreenBounds Bounds);

/// <summary>Immutable rectangle describing screen bounds in device pixels.</summary>
public readonly record struct ScreenBounds(double MinX, double MinY, double Width, double Height)
{
    public double MaxX => MinX + Width;
    public double MaxY => MinY + Height;
}
