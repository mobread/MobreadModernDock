namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Models;

/// <summary>
/// Direct port of DockPositioningService, with the JavaFX Screen dependency
/// abstracted behind IScreenBoundsProvider so the Core stays platform-agnostic.
/// </summary>
public class DockPositioningService
{
    private readonly DockService _dockService;
    private readonly IScreenBoundsProvider? _screenBoundsProvider;

    public DockPositioningService(DockService dockService, IScreenBoundsProvider? screenBoundsProvider = null)
    {
        _dockService = dockService;
        _screenBoundsProvider = screenBoundsProvider;
    }

    public DockPositioningMode GetPositioningMode() => _dockService.GetDock().PositioningMode;

    public void SetPositioningMode(DockPositioningMode positioningMode)
    {
        _dockService.GetDock().PositioningMode = positioningMode;
        _dockService.SaveChanges();
    }

    public DockVerticalAnchor GetVerticalAnchor() => _dockService.GetDock().VerticalAnchor;

    public void SetVerticalAnchor(DockVerticalAnchor verticalAnchor)
    {
        _dockService.GetDock().VerticalAnchor = verticalAnchor;
        _dockService.SaveChanges();
    }

    public DockHorizontalAnchor GetHorizontalAnchor() => _dockService.GetDock().HorizontalAnchor;

    public void SetHorizontalAnchor(DockHorizontalAnchor horizontalAnchor)
    {
        _dockService.GetDock().HorizontalAnchor = horizontalAnchor;
        _dockService.SaveChanges();
    }

    public int GetScreenEdgeSpacing() => _dockService.GetDock().TopSpacing;

    public void SetScreenEdgeSpacing(int spacing)
    {
        SetTopSpacing(spacing);
        SetLeftSpacing(spacing);
        SetRightSpacing(spacing);
        SetBottomSpacing(spacing);
    }

    public int GetTopSpacing() => _dockService.GetDock().TopSpacing;

    public void SetTopSpacing(int spacing)
    {
        _dockService.GetDock().TopSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetLeftSpacing() => _dockService.GetDock().LeftSpacing;

    public void SetLeftSpacing(int spacing)
    {
        _dockService.GetDock().LeftSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetRightSpacing() => _dockService.GetDock().RightSpacing;

    public void SetRightSpacing(int spacing)
    {
        _dockService.GetDock().RightSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetBottomSpacing() => _dockService.GetDock().BottomSpacing;

    public void SetBottomSpacing(int spacing)
    {
        _dockService.GetDock().BottomSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public bool IsDynamicPositioning() => GetPositioningMode() == DockPositioningMode.DYNAMIC;

    /// <summary>
    /// Computes the dock's screen position based on the current positioning mode.
    /// Returns (x, y) in device pixels.
    /// </summary>
    public (double X, double Y) ResolvePosition(double windowWidth, double windowHeight)
    {
        DockModel dock = _dockService.GetDock();

        if (dock.PositioningMode == DockPositioningMode.DYNAMIC)
        {
            var saved = (SnapToPixel(dock.DockPositionX), SnapToPixel(dock.DockPositionY));
            // A saved position that no longer lies on any part of the primary
            // screen (monitor layout changed, or was persisted with a wrong
            // offset) would leave the dock stranded; fall back to the primary
            // screen's bottom-center in that case.
            if (_screenBoundsProvider != null)
            {
                var b = _screenBoundsProvider.GetPrimaryScreenBounds();
                bool onScreen = saved.Item1 + windowWidth > b.MinX && saved.Item1 < b.MaxX
                             && saved.Item2 + windowHeight > b.MinY && saved.Item2 < b.MaxY;
                if (!onScreen)
                    return (SnapToPixel(b.MinX + (b.Width - windowWidth) / 2),
                            SnapToPixel(b.MaxY - windowHeight - dock.BottomSpacing));
            }
            return saved;
        }

        if (_screenBoundsProvider == null)
            return (0, 0);

        var bounds = _screenBoundsProvider.GetPrimaryScreenBounds();
        double x = SnapToPixel(ResolveHorizontalPosition(bounds, windowWidth, dock));
        double y = SnapToPixel(ResolveVerticalPosition(bounds, windowHeight, dock));
        return (x, y);
    }

    /// <summary>Primary screen work area (or a 1920x1080 fallback when no provider is wired).</summary>
    public ScreenBounds GetPrimaryScreenBounds() =>
        _screenBoundsProvider?.GetPrimaryScreenBounds() ?? new ScreenBounds(0, 0, 1920, 1080);

    public static double SnapToPixel(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static double ResolveHorizontalPosition(
        ScreenBounds bounds, double windowWidth, DockModel dock)
    {
        return dock.HorizontalAnchor switch
        {
            DockHorizontalAnchor.LEFT => bounds.MinX + dock.LeftSpacing,
            DockHorizontalAnchor.MIDDLE => bounds.MinX + ((bounds.Width - windowWidth) / 2),
            DockHorizontalAnchor.RIGHT => bounds.MaxX - windowWidth - dock.RightSpacing,
            _ => bounds.MinX
        };
    }

    private static double ResolveVerticalPosition(
        ScreenBounds bounds, double windowHeight, DockModel dock)
    {
        return dock.VerticalAnchor switch
        {
            DockVerticalAnchor.TOP => bounds.MinY + dock.TopSpacing,
            DockVerticalAnchor.MIDDLE => bounds.MinY + ((bounds.Height - windowHeight) / 2),
            DockVerticalAnchor.DOWN => bounds.MaxY - windowHeight - dock.BottomSpacing,
            _ => bounds.MinY
        };
    }
}
