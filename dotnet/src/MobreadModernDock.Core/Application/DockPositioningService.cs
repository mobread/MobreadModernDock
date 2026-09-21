namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Models;

/// <summary>
/// Decides where the dock sits: anchored to a screen edge in STATIC mode, or
/// at the user's dragged position in DYNAMIC mode. Screen geometry is
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

    /// <summary>
    /// Primary monitor's full bounds, taskbar and appbars included. Callers
    /// whose result feeds back into the dock's own position must use this, not
    /// the work area — see <see cref="IScreenBoundsProvider.GetPrimaryMonitorBounds"/>.
    /// </summary>
    public ScreenBounds GetPrimaryMonitorBounds() =>
        _screenBoundsProvider?.GetPrimaryMonitorBounds() ?? new ScreenBounds(0, 0, 1920, 1080);

    // --- #10 per-monitor ---

    public IReadOnlyList<ScreenInfo> GetAllScreens() =>
        _screenBoundsProvider?.GetAllScreens() ?? new[] { new ScreenInfo("primary", true, GetPrimaryScreenBounds()) };

    public ScreenInfo? FindScreen(string id) => GetAllScreens().FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Position for a mirrored dock on a secondary monitor. STATIC mode: the
    /// same anchors against that screen's bounds. DYNAMIC mode: the primary's
    /// position expressed as a fraction of its screen's free space, re-applied
    /// to the target screen — so a dock dragged to bottom-centre on the primary
    /// shows up bottom-centre on every monitor.
    ///
    /// The fraction is taken against the primary's <b>full monitor</b> bounds,
    /// not its work area. The work area already excludes any edge this app
    /// reserved, so using it would make the denominator shrink as soon as the
    /// dock reserved its edge: the fraction would read as 1.0, and a dock the
    /// user parked at the bottom would drift on every recomputation.
    /// </summary>
    public (double X, double Y) ResolvePositionOnScreen(ScreenBounds bounds, double windowWidth, double windowHeight)
    {
        DockModel dock = _dockService.GetDock();
        if (dock.PositioningMode == DockPositioningMode.DYNAMIC && _screenBoundsProvider != null)
        {
            var p = _screenBoundsProvider.GetPrimaryMonitorBounds();
            double freeW = Math.Max(1, p.Width - windowWidth), freeH = Math.Max(1, p.Height - windowHeight);
            double fx = Math.Clamp((dock.DockPositionX - p.MinX) / freeW, 0, 1);
            double fy = Math.Clamp((dock.DockPositionY - p.MinY) / freeH, 0, 1);
            return (SnapToPixel(bounds.MinX + fx * Math.Max(0, bounds.Width - windowWidth)),
                    SnapToPixel(bounds.MinY + fy * Math.Max(0, bounds.Height - windowHeight)));
        }
        return (SnapToPixel(ResolveHorizontalPosition(bounds, windowWidth, dock)),
                SnapToPixel(ResolveVerticalPosition(bounds, windowHeight, dock)));
    }

    public bool GetMirrorOnAllMonitors() => _dockService.GetDock().MirrorOnAllMonitors;

    public void SetMirrorOnAllMonitors(bool value)
    {
        _dockService.GetDock().MirrorOnAllMonitors = value;
        _dockService.SaveChanges();
    }

    public static double SnapToPixel(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Centres a freely-dragged dock along the edge it sits on, leaving the
    /// other axis alone so the dock stays on its edge.
    ///
    /// A horizontal dock centres left-to-right; a vertical dock (which lives
    /// on a side edge) centres top-to-bottom. Centring a vertical dock
    /// horizontally would drag it into the middle of the screen, which is
    /// never what "centre the dock" means.
    ///
    /// Only meaningful in DYNAMIC mode - in STATIC mode the anchors already
    /// decide the position and the dock cannot be dragged off-centre.
    /// </summary>
    public static (double X, double Y) CenterAlongEdge(
        ScreenBounds bounds, double x, double y, double windowWidth, double windowHeight, bool verticalDock)
    {
        if (verticalDock)
        {
            double cy = bounds.MinY + (bounds.Height - windowHeight) / 2;
            return (SnapToPixel(x), SnapToPixel(cy));
        }
        double cx = bounds.MinX + (bounds.Width - windowWidth) / 2;
        return (SnapToPixel(cx), SnapToPixel(y));
    }

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
