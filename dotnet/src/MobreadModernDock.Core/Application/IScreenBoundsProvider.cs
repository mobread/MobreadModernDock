namespace MobreadModernDock.Core.Application;

/// <summary>
/// Port providing screen geometry, abstracting the platform-specific screen
/// enumeration (Avalonia Screen / Win32 monitor enumeration).
/// Coordinates are in device pixels, and exclude the taskbar and other appbars.
/// </summary>
public interface IScreenBoundsProvider
{
    /// <summary>The visual bounds of the primary screen (work area, excluding taskbar).</summary>
    ScreenBounds GetPrimaryScreenBounds();

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
