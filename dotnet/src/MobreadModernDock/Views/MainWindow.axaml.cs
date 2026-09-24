using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Native;
using MobreadModernDock.ViewModels;

namespace MobreadModernDock.Views;

public partial class MainWindow : Window
{
    private DockWindowBehavior? _dockBehavior;
    private AppServices? _appServices;
    private WindowPreviewPopup? _previewPopup;
    private Button? _hoveredButton;
    private Button? _previewAnchor;
    private int _previewRequestId;
    // Identity of the item whose preview is currently shown. _hoveredButton is
    // nulled while the pointer is over the popup, so the close handler cannot
    // rely on it; these are captured when the preview is requested.
    private string _previewExecutablePath = "";
    private string _previewLabel = "";
    private readonly DispatcherTimer _previewHideDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _previewCloseRefresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _positionPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>
    /// How long the pointer must rest on an icon before its window preview
    /// appears. Without a delay a sweep across the dock fires one preview per
    /// icon, which is noisy — especially with magnification on, where it also
    /// fights the zoom animation. User-configurable in Settings; 0 restores
    /// the original load-on-enter behaviour.
    /// </summary>
    private readonly DispatcherTimer _previewShowDelay = new();
    private Button? _pendingPreviewButton;
    private string _pendingPreviewLabel = "";
    private string _pendingPreviewExecutable = "";

    private TimeSpan PreviewShowDelay =>
        TimeSpan.FromMilliseconds(_appServices?.AppearanceService.GetPreviewDelayMs() ?? 0);

    private DockAutoHideController? _autoHide;
    private AppBarReservation? _reservation;   // #4 screen-edge reservation (primary dock only)
    private FolderStackPopup? _folderStack;
    private Button? _lastPressedPinned;

    // --- #10 per-monitor ---
    /// <summary>
    /// When set, this window is a mirror on a secondary monitor: it anchors to
    /// that screen with the STATIC rules, never persists its position, and
    /// ignores the DYNAMIC saved point (which belongs to the primary dock).
    /// </summary>
    public string? MirrorScreenId { get; init; }
    public bool IsMirror => MirrorScreenId != null;

    // --- Drag-to-reorder state for pinned dock icons ---
    private const double ReorderDragThresholdPixels = 6;
    private int _reorderSourceIndex = -1;
    private Point _reorderPressPoint;
    private bool _reorderInProgress;

    public MainWindow()
    {
        InitializeComponent();
        // Button marks PointerPressed as handled (it owns the click), so the
        // reorder gesture subscribes at the ItemsControl level with
        // handledEventsToo — the same approach the Settings list uses.
        PinnedItems.AddHandler(InputElement.PointerPressedEvent, OnPinnedPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        PinnedItems.AddHandler(InputElement.PointerMovedEvent, OnPinnedPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        PinnedItems.AddHandler(InputElement.PointerReleasedEvent, OnPinnedPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        PinnedItems.AddHandler(InputElement.PointerCaptureLostEvent, OnPinnedPointerCaptureLost,
            RoutingStrategies.Bubble, handledEventsToo: true);
        PositionChanged += OnDockPositionChanged;
        _positionPersistTimer.Tick += (_, _) =>
        {
            _positionPersistTimer.Stop();
            PersistDockPosition();
        };
        // The popup hides only when the pointer is neither over the popup nor
        // over a native thumbnail window (thumbnails are separate top-level
        // windows, so leaving the popup onto a thumbnail fires PointerExited).
        _previewHideDebounce.Tick += (_, _) =>
        {
            _previewHideDebounce.Stop();
            if (IsPointerOverPreview())
                _previewHideDebounce.Start();
            else
                HidePreview();
        };
        // After a close request the window may take a moment to disappear (it
        // can show a save prompt), so the row refresh is deferred until it has.
        _previewCloseRefresh.Tick += (_, _) =>
        {
            _previewCloseRefresh.Stop();
            RefreshPreviewAfterClose();
        };
        // Magnification only: the preview waits for the pointer to settle.
        _previewShowDelay.Tick += (_, _) =>
        {
            _previewShowDelay.Stop();
            StartPreviewLoad();
        };
    }

    private bool IsPointerOverPreview()
    {
        if (_previewPopup is not { IsVisible: true } popup) return false;
        if (!User32.GetCursorPos(out POINT pt)) return false;
        return popup.IsPointOverPopup(pt.X, pt.Y);
    }

    /// <summary>Receives the composed application services from the App composition root.</summary>
    public void SetAppServices(AppServices appServices) => _appServices = appServices;

    /// <summary>
    /// Once the native window is created and shown, grab its HWND and apply
    /// the dock-specific Win32 behavior (no-activate, no-taskbar, Win+D defense).
    /// Also applies the saved dock position from the positioning service.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        IPlatformHandle? handle = this.TryGetPlatformHandle();
        if (handle is null)
        {
            UpdateStatus("ERROR: Could not obtain native window handle");
            return;
        }

        _dockBehavior = new DockWindowBehavior(handle.Handle, UpdateStatus);
        _dockBehavior.PlaceForSize = WindowPositionForSize;
        _dockBehavior.Apply(_appServices?.AppearanceService.GetAlwaysOnTop() ?? false);

        // Initialize the dock ViewModel (loads items, starts indicator watcher).
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenSettingsAction = () => OpenSettings(vm);
            vm.RepositionAction = () => ApplyDockPosition();
            vm.LayerRefreshAction = () => { ApplyAlwaysOnTop(); ApplyAutoHideSetting(); SyncPinnedPanel(); ApplyEdgeReservation(); };
            vm.ShowFolderStackAction = ShowFolderStack;
            vm.PreviewDismissAction = HidePreview;
            // The running-apps list is hidden until the first unpinned app
            // opens, and its DockItemsPanel is only realized then - with the
            // panel defaults (horizontal). Re-push orientation/magnification
            // whenever that list appears or changes, or a vertical dock lays
            // its running apps out side by side (and widens the whole dock).
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.HasRunningApps))
                    Dispatcher.UIThread.Post(SyncPinnedPanel, DispatcherPriority.Loaded);
            };
            vm.RunningApps.CollectionChanged += (_, _) =>
                Dispatcher.UIThread.Post(SyncPinnedPanel, DispatcherPriority.Loaded);
            vm.Initialize();
        }

        ApplyDockPosition(force: true);

        _autoHide = new DockAutoHideController(
            behavior: () => _dockBehavior,
            // Physical pixels: the controller compares against GetCursorPos
            // and monitor rects, which are physical too.
            size: () => ScreenGeometry.WindowScreenRect(this) is { Width: > 0 } r
                ? (r.Width, r.Height)
                : ((int)Math.Round(Bounds.Width * RenderScaling), (int)Math.Round(Bounds.Height * RenderScaling)),
            restPosition: RestPosition,
            screenBounds: () =>
            {
                var sb = OwnScreenBounds();
                return ((int)sb.MinX, (int)sb.MinY, (int)sb.MaxX, (int)sb.MaxY);
            },
            blockHide: () => _previewPopup?.IsVisible == true || _folderStack?.IsVisible == true || _reorderInProgress,
            inset: () => { var i = MagnifyInset(); return (i.X, i.Y); });
        ApplyAutoHideSetting();

        // Static anchors must use the finalized window size, which SizeToContent
        // only produces after the first layout pass. Re-apply once layout settles
        // and whenever the dock content resizes the window.
        SizeChanged += OnDockSizeChanged;
        if (IsMirror || _appServices?.PositioningService.IsDynamicPositioning() == false)
            Dispatcher.UIThread.Post(() => ApplyDockPosition(), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(RefreshTooltipPlacement, DispatcherPriority.Loaded);
        // The items panel is realized during the first layout pass, after
        // Initialize() has already run, so seed it once the tree exists.
        Dispatcher.UIThread.Post(SyncPinnedPanel, DispatcherPriority.Loaded);
        // Reservation needs the finalized window rect, which SizeToContent
        // only produces after the first layout pass.
        Dispatcher.UIThread.Post(ApplyEdgeReservation, DispatcherPriority.Loaded);
        // DYNAMIC: startup runs several layout passes (empty bar, items,
        // magnification headroom) that each resize the window. None of them
        // is a user-facing size change, so resize re-centring only starts
        // tracking once they have settled; until then the persisted bar
        // position is simply re-applied with the now-known headroom.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsMirror && _appServices?.PositioningService.IsDynamicPositioning() == true
                && _autoHide is not { IsEnabled: true, IsHidden: true })
            {
                // Persisted point read directly (see KeepBarPlacementAfterResize
                // for why not ResolvePosition).
                var dock = _appServices.DockService.GetDock();
                var inset = MagnifyInset();
                SetScreenPosition((int)dock.DockPositionX - inset.X, (int)dock.DockPositionY - inset.Y);
                ApplyEdgeReservation();
            }
            _lastBarSize = CurrentBarSize();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Transparent headroom either side of the bar, in physical pixels, along
    /// the dock's main axis. Everything that positions the dock works in terms
    /// of the <i>visible bar</i>, so this converts between the two:
    /// <c>window = bar - inset</c>.
    ///
    /// Without it an edge-anchored or edge-snapped dock would sit inset by the
    /// headroom, and the bar would jump sideways whenever the magnification
    /// setting changed the headroom's size.
    /// </summary>
    private PixelPoint MagnifyInset()
    {
        if (DataContext is not MainWindowViewModel vm || vm.MagnifyOverhang <= 0)
            return new PixelPoint(0, 0);
        int px = (int)Math.Round(vm.MagnifyOverhang * RenderScaling);
        return vm.IsVerticalDock ? new PixelPoint(0, px) : new PixelPoint(px, 0);
    }

    /// <summary>
    /// Applies the dock position from the positioning service. In STATIC mode
    /// this re-anchors the dock to the current screen edge; in DYNAMIC mode the
    /// saved position is only applied once at startup (force) so the user's
    /// drags are never overridden by subsequent refreshes.
    /// </summary>
    private void ApplyDockPosition(bool force = false)
    {
        if (_appServices == null) return;
        if (!IsMirror && !force && _appServices.PositioningService.IsDynamicPositioning()) return;
        // Resolve against the visible bar, then step back by the headroom.
        // Screen bounds are physical pixels; Width/Height are DIPs, so the
        // bar size must be scaled first or a RIGHT/DOWN-anchored dock lands
        // (1 - 1/scale) of its size past the screen edge on a 125% display.
        var inset = MagnifyInset();
        var bar = CurrentBarSize();
        var (bx, by) = ResolveOwnPosition(bar.W, bar.H);
        var (x, y) = (bx - inset.X, by - inset.Y);
        if (_autoHide is { IsEnabled: true, IsHidden: true })
        {
            _autoHide.OnLayoutChanged();
            return;
        }
        SetScreenPosition((int)x, (int)y);
        ApplyEdgeReservation();
    }

    /// <summary>Primary dock: the positioning service's answer. Mirror: static anchors on its own screen.</summary>
    private (double X, double Y) ResolveOwnPosition(double w, double h)
    {
        var pos = _appServices!.PositioningService;
        if (!IsMirror) return pos.ResolvePosition(w, h);
        var bounds = OwnScreenBounds();
        return pos.ResolvePositionOnScreen(bounds, w, h);
    }

    /// <summary>
    /// The bounds this window positions itself against.
    ///
    /// While we reserve a screen edge this must be the <b>full monitor</b>, not
    /// the work area: the work area already excludes our own reserved strip, so
    /// re-resolving against it walks the dock inward by the strip's thickness on
    /// every pass (and the dock then reads as undocked and drops the
    /// reservation). Without a reservation the work area is right, so the dock
    /// still respects the real taskbar and other appbars.
    /// </summary>
    private ScreenBounds OwnScreenBounds()
    {
        var pos = _appServices!.PositioningService;
        bool reserving = _appServices.AppearanceService.GetReserveScreenEdge()
                         && !_appServices.AppearanceService.GetAutoHide();

        if (IsMirror && pos.FindScreen(MirrorScreenId!) is { } s)
        {
            if (!reserving) return s.Bounds;
            // Full bounds of the monitor this mirror lives on.
            var centre = new PixelPoint(
                (int)(s.Bounds.MinX + s.Bounds.Width / 2),
                (int)(s.Bounds.MinY + s.Bounds.Height / 2));
            var mon = ScreenGeometry.MonitorAreaAt(centre);
            return new ScreenBounds(mon.X, mon.Y, mon.Width, mon.Height);
        }

        return reserving ? pos.GetPrimaryMonitorBounds() : pos.GetPrimaryScreenBounds();
    }

    /// <summary>
    /// Where the dock sits when fully shown: the anchored position in STATIC
    /// mode, the persisted position in DYNAMIC mode. Auto-hide slides away
    /// from and back to this point.
    /// </summary>
    private (int X, int Y) RestPosition()
    {
        if (_appServices == null) return GetScreenPosition();
        var inset = MagnifyInset();
        var bar = CurrentBarSize();
        var (bx, by) = ResolveOwnPosition(bar.W, bar.H);
        return ((int)bx - inset.X, (int)by - inset.Y);
    }

    private void ApplyAutoHideSetting()
    {
        if (_appServices == null || _autoHide == null) return;
        _autoHide.SetEnabled(_appServices.AppearanceService.GetAutoHide());
    }

    /// <summary>
    /// Positions the window in absolute screen coordinates. Once attached to
    /// the desktop, Avalonia's Position is parent-relative and lands on the
    /// wrong monitor when the primary display is not at the virtual origin;
    /// DockWindowBehavior converts through the parent so it stays absolute.
    /// </summary>
    private void SetScreenPosition(int x, int y)
    {
        if (_dockBehavior != null)
            _dockBehavior.MoveToScreen(x, y);
        else
            Position = new PixelPoint(x, y);
        RefreshTooltipPlacement();
    }

    private (int X, int Y) GetScreenPosition() =>
        _dockBehavior?.GetScreenPosition() ?? (Position.X, Position.Y);

    /// <summary>
    /// Screen rect of the <i>visible bar</i>: the window rect with the
    /// transparent magnification headroom taken off each side.
    ///
    /// Edge snapping, centring, appbar reservation and tooltip placement all
    /// have to reason about what the user can see. Using the raw window rect
    /// would make a "snapped" dock sit a headroom-width away from the edge and
    /// reserve a band of empty space.
    /// </summary>
    private PixelRect VisibleBarScreenRect()
    {
        var rect = ScreenGeometry.WindowScreenRect(this);
        var inset = MagnifyInset();
        return new PixelRect(
            rect.X + inset.X, rect.Y + inset.Y,
            Math.Max(1, rect.Width - 2 * inset.X),
            Math.Max(1, rect.Height - 2 * inset.Y));
    }

    /// <summary>
    /// The painted bar only: <see cref="VisibleBarScreenRect"/> minus the
    /// transparent cross-axis headroom on the far side (above a horizontal
    /// dock, left of a vertical one) that the bounce and magnification growth
    /// paint into. Used for the appbar reservation, so the reserved strip
    /// never grows with the magnification slider - magnified icons overlap
    /// whatever window sits above the bar instead of pushing it up.
    /// </summary>
    private PixelRect PaintedBarScreenRect()
    {
        var rect = VisibleBarScreenRect();
        if (DataContext is not MainWindowViewModel vm) return rect;
        var m = vm.DockBarMargin;
        if (vm.IsVerticalDock)
        {
            // Growth headroom sits on the side away from the resting edge.
            if (vm.VerticalRestsOnLeft)
            {
                int right = (int)Math.Round(m.Right * RenderScaling);
                return new PixelRect(rect.X, rect.Y, Math.Max(1, rect.Width - right), rect.Height);
            }
            int left = (int)Math.Round(m.Left * RenderScaling);
            return new PixelRect(rect.X + left, rect.Y, Math.Max(1, rect.Width - left), rect.Height);
        }
        int top = (int)Math.Round(m.Top * RenderScaling);
        return new PixelRect(rect.X, rect.Y + top, rect.Width, Math.Max(1, rect.Height - top));
    }

    private void OnDockSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        EnsureAutoSize();
        RefreshTooltipPlacement();
        if (IsMirror || _appServices?.PositioningService.IsDynamicPositioning() == false)
            ApplyDockPosition();
        else
            KeepBarPlacementAfterResize();
        // A taller/wider bar must reserve a correspondingly bigger strip.
        ApplyEdgeReservation();
    }

    /// <summary>Visible-bar size (physical px) as of the last size change; null until startup layout has settled.</summary>
    private (int W, int H)? _lastBarSize;

    private (int W, int H) CurrentBarSize()
    {
        var inset = MagnifyInset();
        double scale = Math.Max(0.01, RenderScaling);
        return ((int)Math.Round(Bounds.Width * scale) - 2 * inset.X,
                (int)Math.Round(Bounds.Height * scale) - 2 * inset.Y);
    }

    /// <summary>
    /// DYNAMIC mode: a freely-placed dock has no anchor to re-resolve against,
    /// and the window is top-left anchored, so any change to the bar's size
    /// (icon size, padding, items, or the magnification headroom) would leave
    /// the bar drifting right/down from where the user put it. A centred bar
    /// is re-centred and an edge-flush bar stays flush, per axis - see
    /// <see cref="DockPositioningService.KeepPlacementAfterResize"/>.
    ///
    /// Everything is done in <i>visible bar</i> coordinates from the persisted
    /// rest position, not the live window position: while auto-hide has the
    /// dock slid off-screen (which is exactly when the user is in Settings
    /// changing sizes) the live position is the hidden one.
    /// </summary>
    private void KeepBarPlacementAfterResize()
    {
        if (_appServices == null || IsMirror) return;
        // Not armed until startup layout has settled (see OnOpened).
        if (_lastBarSize is not { } oldBar) return;
        var inset = MagnifyInset();
        var newBar = CurrentBarSize();
        _lastBarSize = newBar;

        var dock = _appServices.DockService.GetDock();
        var (ox, oy) = (dock.DockPositionX, dock.DockPositionY);
        var (bx, by) = BarPlacementForResize(oldBar, newBar);
        if (newBar != oldBar && (bx != (int)ox || by != (int)oy))
        {
            _appServices.DockService.SetDockPosition(bx, by);
            App.RepositionMirrorDocks();
        }

        if (_autoHide is { IsEnabled: true, IsHidden: true })
        {
            _autoHide.OnLayoutChanged();
            return;
        }
        SetScreenPosition(bx - inset.X, by - inset.Y);
    }

    /// <summary>
    /// Where the visible bar goes when it changes from <paramref name="oldBar"/>
    /// to <paramref name="newBar"/> (DYNAMIC mode). Pure: shared by the
    /// post-resize path above and by <see cref="WindowPositionForSize"/>,
    /// which places the window in the same step as the resize.
    /// </summary>
    private (int X, int Y) BarPlacementForResize((int W, int H) oldBar, (int W, int H) newBar)
    {
        // Rest position of the bar before this resize: the persisted point,
        // read directly. NOT ResolvePosition(): its off-screen fallback checks
        // the *work area*, which our own reserved strip has already shrunk to
        // the bar's top edge, so a bottom-flush dock reads as off-screen and
        // gets re-anchored a bar-height higher on every resize.
        var dock = _appServices!.DockService.GetDock();
        var (ox, oy) = (dock.DockPositionX, dock.DockPositionY);
        // Only the headroom changed: the bar stays put, the window moves.
        if (newBar == oldBar) return ((int)ox, (int)oy);

        var centre = new PixelPoint((int)ox + oldBar.W / 2, (int)oy + oldBar.H / 2);
        bool reserving = _appServices.AppearanceService.GetReserveScreenEdge()
                         && !_appServices.AppearanceService.GetAutoHide();
        var area = reserving ? ScreenGeometry.MonitorAreaAt(centre) : ScreenGeometry.WorkAreaAt(centre);
        var bounds = new ScreenBounds(area.X, area.Y, area.Width, area.Height);
        var (nx, ny) = DockPositioningService.KeepPlacementAfterResize(
            bounds, ox, oy, oldBar.W, oldBar.H, newBar.W, newBar.H);
        return ((int)nx, (int)ny);
    }

    /// <summary>
    /// Window top-left (physical, absolute) for a window of the given
    /// physical size, or null when the caller should not interfere (auto-hide
    /// has the dock slid away, or startup layout has not settled). Hooked into
    /// WM_WINDOWPOSCHANGING so a resize lands in place instead of first
    /// overhanging the screen edge and being pulled back - which, repeated
    /// per slider step, made the dock shake.
    /// </summary>
    private (int X, int Y)? WindowPositionForSize(int width, int height)
    {
        if (_appServices == null) return null;
        if (_autoHide is { IsEnabled: true, IsHidden: true }) return null;
        var inset = MagnifyInset();
        var newBar = (W: width - 2 * inset.X, H: height - 2 * inset.Y);

        if (IsMirror || !_appServices.PositioningService.IsDynamicPositioning())
        {
            var (bx, by) = ResolveOwnPosition(newBar.W, newBar.H);
            return ((int)bx - inset.X, (int)by - inset.Y);
        }
        if (_lastBarSize is not { } oldBar) return null;
        var (x, y) = BarPlacementForResize(oldBar, newBar);
        return (x - inset.X, y - inset.Y);
    }

    /// <summary>
    /// Pushes the row/column count and orientation into the pinned items'
    /// layout panel. An ItemsPanelTemplate has no DataContext, so these cannot
    /// be bound in XAML; the panel is also created lazily, hence the lookup on
    /// each refresh rather than a cached field.
    /// </summary>
    private void SyncPinnedPanel()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var pinned = FindPanel(PinnedItems);
        var running = FindPanel(RunningItems);
        if (pinned == null) return;

        // Magnification is single-line only; the service already folds that in.
        double scale = _appServices?.AppearanceService.GetMagnifyIcons() == true
            ? _appServices.AppearanceService.GetMagnifyScale()
            : 1.0;

        // Orientation/lines on every realized panel, visible or not: a hidden
        // running list must already be vertical the moment it appears.
        // (Panels() below skips hidden ones - right for the magnification row.)
        foreach (var p in new[] { pinned, running })
        {
            if (p == null) continue;
            p.Lines = vm.DockLines;
            p.IsVertical = vm.IsVerticalDock;
            p.RestsOnLeft = vm.VerticalRestsOnLeft;
        }

        foreach (var (panel, host) in Panels(pinned, running))
        {
            panel.MagnifyScale = scale;
            // The per-item hover zoom must stand down while the panel is scaling.
            host.Classes.Set("magnified", scale > 1.0);
            if (scale <= 1.0) { panel.UpdateMagnification(null); ClearDividerTransform(); }
        }
        SyncRowContext(pinned, running);
        SyncMagnifyOverhang(vm, pinned, running);
    }

    /// <summary>
    /// Sizes the transparent side headroom that keeps magnified end icons on
    /// screen. Magnification pushes the row's end items outward by up to half
    /// the row's expansion, and a window cannot paint outside its own bounds -
    /// without this the end icons are sliced off at the window edge.
    ///
    /// Computed from the whole row (both panels plus the divider) so it matches
    /// the geometry the panels actually arrange, and re-run from
    /// <see cref="SyncPinnedPanel"/>, which already fires whenever the items or
    /// the appearance settings change.
    /// </summary>
    private void SyncMagnifyOverhang(
        MainWindowViewModel vm, DockItemsPanel pinned, DockItemsPanel? running)
    {
        double overhang = 0;
        double growth = 0;
        if (pinned.MagnifyScale > 1.0 && vm.DockLines <= 1)
        {
            // The same row the panels magnify over: ours, the divider gap, then
            // the running apps (RestSizes already excludes a hidden panel).
            var row = new List<double>(RestSizes(pinned));
            if (running != null && RunningItems.IsVisible)
            {
                row.Add(GapBetweenPanels(pinned, running));
                row.AddRange(RestSizes(running));
            }

            double influence = 0;
            foreach (var s in row) influence = Math.Max(influence, s);
            influence *= DockMagnification.InfluenceIcons;

            overhang = DockMagnification.MaxOverhang(row, pinned.MagnifyScale, influence);
            overhang = Math.Ceiling(overhang);

            // Cross-axis: the icon under the pointer scales from the resting
            // edge, so it grows away from the screen edge by size × (scale − 1)
            // - straight through the bar's padding and, unless the window has
            // room, off the top of the window where it is sliced flat.
            double largest = 0;
            foreach (var s in row) largest = Math.Max(largest, s);
            growth = Math.Ceiling(largest * (pinned.MagnifyScale - 1.0));
        }

        double cross = Math.Max(MainWindowViewModel.BounceHeadroom, growth + 4);
        bool crossChanged = Math.Abs(vm.CrossHeadroom - cross) >= 0.5;
        if (crossChanged) vm.CrossHeadroom = cross;

        if (Math.Abs(vm.MagnifyOverhang - overhang) < 0.5)
        {
            // Only the cross headroom changed: the window grows on its far
            // side; the same re-placement below keeps the bar on its edge.
            if (!crossChanged) return;
        }

        // Keep the *bar* where it is. The window grows/shrinks by the headroom
        // delta on each side; the resulting SizeChanged re-places the window
        // so the bar does not move: KeepBarPlacementAfterResize in DYNAMIC mode
        // (bar size unchanged -> same bar position, new inset), and
        // ApplyDockPosition against the anchor in STATIC mode / for mirrors.
        vm.MagnifyOverhang = overhang;

        if (_appServices?.PositioningService.IsDynamicPositioning() == true && !IsMirror)
            return;

        // STATIC / mirrors: the anchor is authoritative, so re-place against it.
        Dispatcher.UIThread.Post(() => ApplyDockPosition(force: true), DispatcherPriority.Loaded);
    }

    private static DockItemsPanel? FindPanel(ItemsControl host) =>
        host.GetVisualDescendants().OfType<DockItemsPanel>().FirstOrDefault();

    /// <summary>The realized dock panels in row order, each with its ItemsControl.</summary>
    private IEnumerable<(DockItemsPanel Panel, ItemsControl Host)> Panels(
        DockItemsPanel? pinned, DockItemsPanel? running)
    {
        if (pinned != null) yield return (pinned, PinnedItems);
        // Only while it is actually on screen: an invisible running list must
        // not contribute rest sizes to the row.
        if (running != null && RunningItems.IsVisible) yield return (running, RunningItems);
    }

    /// <summary>
    /// Tells each panel about the items on the other side of the divider.
    /// Magnification must be computed over the whole visual row, otherwise the
    /// falloff restarts at the seam and each group re-centres into the other
    /// (see DockItemsPanel.MagnifyLeading).
    ///
    /// The divider (and any margin between the two controls) is injected as a
    /// synthetic leading entry so row coordinates match real screen positions;
    /// without it every running icon would be magnified as though it sat a
    /// divider-width to the left of where it is drawn.
    /// </summary>
    private void SyncRowContext(DockItemsPanel? pinned, DockItemsPanel? running)
    {
        bool runningLive = running != null && RunningItems.IsVisible;
        var pinnedSizes = pinned != null ? RestSizes(pinned) : Array.Empty<double>();

        if (pinned != null)
        {
            pinned.MagnifyLeading = Array.Empty<double>();
            pinned.MagnifyTrailing = runningLive
                ? Prepend(GapBetweenPanels(pinned, running!), RestSizes(running!))
                : Array.Empty<double>();
            // The pinned panel owns the row computation, so it reports the
            // divider's transform back (index: straight after our children).
            pinned.MagnifyExternalRowIndex = runningLive ? pinnedSizes.Length : null;
            pinned.ExternalTransformComputed = runningLive ? ApplyDividerTransform : null;
        }
        if (runningLive)
        {
            running!.MagnifyLeading = Append(pinnedSizes, GapBetweenPanels(pinned, running));
            running.MagnifyTrailing = Array.Empty<double>();
            // Only one panel may drive the divider or they would fight.
            running.MagnifyExternalRowIndex = null;
            running.ExternalTransformComputed = null;
        }
    }

    /// <summary>
    /// Applies the row-computed scale/offset to the pinned↔running divider.
    /// It is a plain Image in the outer StackPanel rather than a child of
    /// either items panel, so nothing arranges it and it would otherwise be
    /// the one separator in the dock that never magnified.
    /// </summary>
    private void ApplyDividerTransform(double scale, double offset)
    {
        bool vertical = _appServices?.AppearanceService.GetVerticalDock() == true;
        var divider = vertical ? RunningDividerV : RunningDividerH;
        if (divider is null || !divider.IsVisible) return;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(vertical
            ? new TranslateTransform(0, offset)
            : new TranslateTransform(offset, 0));
        // Same resting edge as the icons (DockItemsPanel.RestsOnLeft).
        if (vertical && DataContext is MainWindowViewModel vm)
            divider.RenderTransformOrigin = new RelativePoint(vm.VerticalRestsOnLeft ? 0 : 1, 0.5, RelativeUnit.Relative);
        divider.RenderTransform = group;
    }

    /// <summary>Returns the divider to its rest size (magnification off or pointer away).</summary>
    private void ClearDividerTransform()
    {
        if (RunningDividerH is { } h) h.RenderTransform = null;
        if (RunningDividerV is { } v) v.RenderTransform = null;
    }

    /// <summary>
    /// Distance along the dock's main axis from the end of the pinned panel to
    /// the start of the running panel: the divider image plus any margins.
    /// Measured from the live visual tree, so it follows the icon size and the
    /// separator's visibility without a second source of truth.
    /// </summary>
    private double GapBetweenPanels(DockItemsPanel? pinned, DockItemsPanel running)
    {
        if (pinned is null) return 0;
        var from = pinned.TranslatePoint(new Point(pinned.Bounds.Width, pinned.Bounds.Height), this);
        var to = running.TranslatePoint(new Point(0, 0), this);
        if (from is not { } a || to is not { } b) return 0;
        double gap = running.IsVertical ? b.Y - a.Y : b.X - a.X;
        return Math.Max(0, gap);
    }

    private static double[] Prepend(double first, IReadOnlyList<double> rest)
    {
        var result = new double[rest.Count + 1];
        result[0] = first;
        for (int i = 0; i < rest.Count; i++) result[i + 1] = rest[i];
        return result;
    }

    private static double[] Append(IReadOnlyList<double> head, double last)
    {
        var result = new double[head.Count + 1];
        for (int i = 0; i < head.Count; i++) result[i] = head[i];
        result[^1] = last;
        return result;
    }

    /// <summary>Each child's rest size along the dock's main axis.</summary>
    private static double[] RestSizes(DockItemsPanel panel)
    {
        var sizes = new double[panel.Children.Count];
        for (int i = 0; i < sizes.Length; i++)
        {
            var d = panel.Children[i].DesiredSize;
            sizes[i] = panel.IsVertical ? d.Height : d.Width;
        }
        return sizes;
    }

    /// <summary>
    /// Feeds the pointer position to both items panels so they magnify as one
    /// row. The position is converted to row coordinates - the origin is the
    /// start of the pinned panel - so an icon's scale depends only on where it
    /// sits on screen, not which ItemsControl owns it.
    /// </summary>
    private void UpdateMagnifier(PointerEventArgs e)
    {
        var pinned = FindPanel(PinnedItems);
        var running = FindPanel(RunningItems);
        if (pinned is null) return;

        // Re-read the setting here rather than trusting a value pushed in
        // earlier: SyncPinnedPanel can run before the panel is realized, and
        // a stale 1.0 would silently disable the effect for the whole session.
        double scale = _appServices?.AppearanceService.GetMagnifyIcons() == true
            ? _appServices.AppearanceService.GetMagnifyScale()
            : 1.0;

        foreach (var (panel, host) in Panels(pinned, running))
        {
            if (panel.MagnifyScale != scale)
            {
                panel.MagnifyScale = scale;
                host.Classes.Set("magnified", scale > 1.0);
            }
        }

        if (scale <= 1.0)
        {
            pinned.UpdateMagnification(null);
            running?.UpdateMagnification(null);
            ClearDividerTransform();
            return;
        }

        // Rest sizes change as apps open and close, so refresh the row context
        // on every move rather than only when the items collection changes.
        SyncRowContext(pinned, running);

        // Row origin = the pinned panel's origin. Each panel is told the same
        // row-space pointer; it indexes its own slice via MagnifyLeading.
        var p = e.GetPosition(pinned);
        double pointer = pinned.IsVertical ? p.Y : p.X;
        pinned.UpdateMagnification(pointer);

        if (running != null && RunningItems.IsVisible)
        {
            // The running panel's children are offset from the row origin by
            // the pinned items plus the divider, which its MagnifyLeading
            // already accounts for - so it gets the same row-space value.
            running.UpdateMagnification(pointer);
        }
    }

    private void ClearMagnifier()
    {
        FindPanel(PinnedItems)?.UpdateMagnification(null);
        FindPanel(RunningItems)?.UpdateMagnification(null);
        ClearDividerTransform();
    }

    private void OnItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not DockItemViewModel vm || vm.Item is not DockProgramItemModel item)
            return;

        SchedulePreview(button, vm.Label, item.ExecutablePath);
    }

    /// <summary>
    /// Arms the window preview for an icon. With magnification on the load is
    /// held back until the pointer rests (see <see cref="PreviewShowDelay"/>);
    /// any preview still on screen is dismissed first so the wait never shows
    /// the previous icon's windows.
    /// </summary>
    private void SchedulePreview(Button button, string label, string executablePath)
    {
        _previewHideDebounce.Stop();
        _previewShowDelay.Stop();

        var delay = PreviewShowDelay;
        if (delay > TimeSpan.Zero && _previewPopup?.IsVisible == true)
            HidePreview(); // clears _hoveredButton, so set it after

        _hoveredButton = button;
        _pendingPreviewButton = button;
        _pendingPreviewLabel = label;
        _pendingPreviewExecutable = executablePath;
        _previewExecutablePath = executablePath;
        _previewLabel = label;

        if (delay <= TimeSpan.Zero)
        {
            StartPreviewLoad();
            return;
        }

        _previewShowDelay.Interval = delay;
        _previewShowDelay.Start();
    }

    /// <summary>Kicks off the off-thread window query for the pending icon.</summary>
    private void StartPreviewLoad()
    {
        if (_appServices == null) return;
        var button = _pendingPreviewButton;
        // The pointer may have moved on while the delay ran.
        if (button == null || !ReferenceEquals(_hoveredButton, button)) return;

        string label = _pendingPreviewLabel;
        string executablePath = _pendingPreviewExecutable;
        int requestId = ++_previewRequestId;

        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                windows = _appServices.WindowPreviewService.LoadPreview(
                    new DockProgramItemModel(label, executablePath));
            }
            catch (Exception)
            {
                windows = new List<WindowInfo>();
            }
            Dispatcher.UIThread.Post(() => OnPreviewLoaded(requestId, button, label, windows));
        });
    }

    private void OnRunningAppPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel vm) return;

        SchedulePreview(button, vm.Label, vm.ExecutablePath);
    }

    /// <summary>
    /// Clicking an unpinned running app activates its window when it has exactly
    /// one open window (taskbar-style); with multiple windows nothing happens —
    /// the preview popup still offers per-window control via hover.
    /// </summary>
    private void OnRunningAppClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel vm) return;
        (DataContext as MainWindowViewModel)?.ClearAttention(vm.ExecutablePath);

        string executablePath = vm.ExecutablePath;
        List<WindowInfo> windows;
        try
        {
            windows = _appServices.WindowPreviewService.LoadPreview(
                new DockProgramItemModel(vm.Label, executablePath));
        }
        catch (Exception)
        {
            return;
        }
        if (windows.Count != 1) return;

        ++_previewRequestId;
        RestoreTooltip();
        _previewPopup?.HideNow();
        _hoveredButton = null;
        _appServices.WindowPreviewService.Activate(windows[0]);
    }

    private void OnPreviewLoaded(int requestId, Button button, string label,
        List<WindowInfo> windows)
    {
        if (requestId != _previewRequestId || _hoveredButton != button) return;
        if (windows.Count == 0)
        {
            if (_previewPopup?.IsVisible == true)
                HidePreview();
            return;
        }
        if (_appServices == null) return;
        _previewAnchor = button;

        var appearance = _appServices.AppearanceService;
        if (_previewPopup == null)
        {
            _previewPopup = new WindowPreviewPopup();
            _previewPopup.ThumbnailClicked = hwnd => OnPopupThumbnailClicked(hwnd);
            _previewPopup.CloseRequested = hwnd => OnPopupCloseRequested(hwnd);
            _previewPopup.PointerEnteredCallback = OnPopupPointerEntered;
            _previewPopup.PointerExitedCallback = OnPopupPointerExited;
        }
        // Re-hovering the same item (e.g. after visiting the popup) must not
        // tear down and rebuild the live thumbnails: that races with the hide
        // debounce and can leave rows blank. Keep the already-shown popup.
        if (_previewPopup.IsVisible && _previewPopup.MatchesSource(windows))
        {
            _previewPopup.CancelHide();
            SuppressTooltip(button);
            return;
        }
        _previewPopup.ShowFor(windows, label,
            appearance.GetDockColorRGB(), appearance.GetDockBorderRounding(),
            appearance.GetDockTransparencyPercentage() / 100.0, button,
            verticalDock: _appServices.AppearanceService.GetVerticalDock(),
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor(),
            sizePercent: appearance.GetPreviewSizePercent());

        // The preview wins over the tooltip: both are anchored to the same icon
        // and now open on the same delay, so they overlap. The preview already
        // shows the app name on every row, making the tooltip pure noise.
        SuppressTooltip(button);
    }

    /// <summary>
    /// Hides an item's tooltip while its preview is on screen. The preview is
    /// loaded asynchronously, so the tooltip's own timer can fire *after* the
    /// popup appears and land the label right on top of it; pushing the delay
    /// out of reach vetoes that without touching the tip itself.
    ///
    /// Do NOT clear the tip (<c>ToolTip.SetTip(item, null)</c>) to achieve this:
    /// mutating the hovered control mid-hover makes Avalonia re-evaluate the
    /// pointer, which fires PointerExited on the icon and tears the preview
    /// straight back down.
    /// </summary>
    private void SuppressTooltip(Control? item)
    {
        if (item is null) return;
        ToolTip.SetIsOpen(item, false);
        if (_suppressedTooltip is not null) return;
        _suppressedTooltip = item;
        ToolTip.SetShowDelay(item, int.MaxValue);
    }

    /// <summary>The item whose tooltip is currently vetoed.</summary>
    private Control? _suppressedTooltip;

    /// <summary>
    /// Re-arms a tooltip vetoed by <see cref="SuppressTooltip"/>. The delay is
    /// restored from the view model rather than a captured value, so a change
    /// to the setting while a preview was open is picked up.
    /// </summary>
    private void RestoreTooltip()
    {
        if (_suppressedTooltip is not { } item) return;
        _suppressedTooltip = null;
        int delay = (DataContext as MainWindowViewModel)?.PreviewDelayMs ?? 400;
        ToolTip.SetShowDelay(item, delay);
    }

    private void OnItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredButton == sender) _hoveredButton = null;
        _previewHideDebounce.Stop();
        // A queued (not yet shown) preview belongs to the icon being left, so
        // drop it — otherwise it would pop up a second later over nothing.
        if (ReferenceEquals(_pendingPreviewButton, sender))
        {
            _previewShowDelay.Stop();
            _pendingPreviewButton = null;
        }
        // During fast icon-to-icon moves the previous icon's Exit can be
        // delivered AFTER the next icon's Enter. Re-arming the hide timer then
        // hides the popup 80ms later (the cursor is over the new icon, not the
        // popup) and the requestId bump drops the pending preview load — the
        // popup would never show again until the pointer leaves and re-enters.
        if (_hoveredButton == null)
            _previewHideDebounce.Start();
    }

    /// <summary>Folder item clicked: toggle its stack popup anchored to the icon.</summary>
    private bool ShowFolderStack(DockItemViewModel vm)
    {
        if (_appServices == null || vm.Item is not DockFolderItemModel folder) return false;
        if (!System.IO.Directory.Exists(folder.FolderPath)) return false;
        // Resolve the icon's Button from the item container (works for real
        // clicks and for accessibility/automation invokes alike).
        Button? anchor = null;
        if (DataContext is MainWindowViewModel mainVm)
        {
            int idx = mainVm.Items.IndexOf(vm);
            if (idx >= 0 && PinnedItems.ContainerFromIndex(idx) is Control container)
                anchor = container as Button ?? container.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        }
        anchor ??= _lastPressedPinned;
        if (anchor == null) return false;

        _folderStack ??= new FolderStackPopup();
        if (_folderStack.IsShowingFolder(folder.FolderPath))
        {
            _folderStack.HidePopup();
            return true;
        }
        HidePreview();
        _folderStack.ShowFor(_appServices, folder.FolderPath, vm.Label, anchor,
            _appServices.AppearanceService.GetVerticalDock());
        return true;
    }

    private void HidePreview()
    {
        ++_previewRequestId;
        _previewShowDelay.Stop();
        _pendingPreviewButton = null;
        RestoreTooltip();
        _previewPopup?.HidePopup();
        _hoveredButton = null;
    }

    private void OnPopupPointerEntered()
    {
        _previewHideDebounce.Stop();
        // Pointer returned during a fade-out: reverse it instead of hiding.
        _previewPopup?.CancelHide();
    }

    private void OnPopupPointerExited()
    {
        _previewHideDebounce.Stop();
        // Returning to a dock item (pointer now over the button) fires this
        // after OnItemPointerEntered already re-stopped the hide timer. Don't
        // re-arm it in that case: the item's own pointer handling owns the
        // hide decision, and re-arming here hides the popup 90ms later even
        // though the cursor is over the very item that shows the preview.
        if (_hoveredButton == null)
            _previewHideDebounce.Start();
    }

    private void OnPopupThumbnailClicked(IntPtr sourceHwnd)
    {
        // Click-to-activate: hide instantly (no fade), release the pointer
        // capture held by the pressed row, then bring the window forward.
        ++_previewRequestId;
        RestoreTooltip();
        _previewPopup?.HideNow();
        _hoveredButton = null;
        if (sourceHwnd != IntPtr.Zero)
            _appServices?.WindowPreviewService.Activate(new WindowInfo(sourceHwnd, ""));
    }

    /// <summary>
    /// Close button clicked: ask the source window to close (WM_CLOSE, the same
    /// message the taskbar sends) and defer a row refresh so the popup reflects
    /// the window actually disappearing.
    /// </summary>
    private void OnPopupCloseRequested(IntPtr sourceHwnd)
    {
        if (sourceHwnd == IntPtr.Zero || _appServices == null) return;
        _appServices.WindowPreviewService.Close(new WindowInfo(sourceHwnd, ""));
        _previewCloseRefresh.Stop();
        _previewCloseRefresh.Start();
    }

    /// <summary>
    /// Re-queries the shown item's open windows after a close request: if any
    /// remain, the popup rows are rebuilt without the closed window; if the
    /// closed window was the last one, the whole popup hides. Runs on the UI
    /// thread with the source query executed off it.
    /// </summary>
    private void RefreshPreviewAfterClose()
    {
        if (_appServices == null || _previewPopup == null) return;
        if (_previewPopup.IsVisible != true) return;

        string executablePath = _previewExecutablePath;
        if (string.IsNullOrEmpty(executablePath)) return;

        int requestId = ++_previewRequestId;
        string label = _previewLabel;
        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                windows = _appServices.WindowPreviewService.LoadPreview(
                    new DockProgramItemModel(label, executablePath));
            }
            catch (Exception)
            {
                windows = new List<WindowInfo>();
            }
            Dispatcher.UIThread.Post(() => OnPreviewRefreshed(requestId, label, windows));
        });
    }

    private void OnPreviewRefreshed(int requestId, string label, List<WindowInfo> windows)
    {
        if (requestId != _previewRequestId || _previewPopup is not { IsVisible: true }) return;
        if (_previewPopup == null) return;
        if (windows.Count == 0)
        {
            ++_previewRequestId;
            RestoreTooltip();
            _previewPopup.HideNow();
            _hoveredButton = null;
            return;
        }
        if (_appServices == null) return;
        // Rebuild the popup without the closed window. The pointer is over the
        // popup, so it stays open; CancelHide guards against an in-flight fade.
        var anchor = _previewAnchor ?? _hoveredButton;
        if (anchor == null) return;
        _previewPopup.CancelHide();
        var appearance = _appServices.AppearanceService;
        _previewPopup.ShowFor(windows, label,
            appearance.GetDockColorRGB(), appearance.GetDockBorderRounding(),
            appearance.GetDockTransparencyPercentage() / 100.0, anchor,
            verticalDock: _appServices.AppearanceService.GetVerticalDock(),
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor(),
            sizePercent: appearance.GetPreviewSizePercent());
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _autoHide?.OnPointerEntered();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateMagnifier(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearMagnifier();
        _autoHide?.OnPointerExited();
    }

    /// <summary>Allows dragging the borderless dock window (DYNAMIC mode only).</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Don't start a window drag when pressing a dock item button — the
        // button must receive the click. Drag only when pressing the dock
        // background itself.
        if (!e.Pointer.IsPrimary)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() != null)
            return;
        // In STATIC mode the dock is anchored to the screen and must not be
        // dragged; dragging is only meaningful in DYNAMIC mode.
        if (_appServices == null || !_appServices.PositioningService.IsDynamicPositioning())
            return;
        if (IsLocked) return;
        BeginMoveDrag(e);
        // Win32 runs the move loop synchronously, so the drag is over here.
        EnsureAutoSize();
    }

    /// <summary>
    /// The dock must always size to its content. Avalonia clears the
    /// SizeToContent flag for any dimension that changes while the OS
    /// move/size loop is active (it reads that as the user resizing), and a
    /// drag can resize the bar mid-loop (crossing the screen's centre flips a
    /// vertical dock's resting side and its headroom). With Height cleared the
    /// window stays at its old height, so raising Magnification squeezed the
    /// bar and the re-centring walked the dock down the screen.
    /// </summary>
    private void EnsureAutoSize()
    {
        if (SizeToContent != SizeToContent.WidthAndHeight)
            SizeToContent = SizeToContent.WidthAndHeight;
    }

    // --- Drag-to-reorder pinned icons directly on the dock bar ---

    private bool IsVerticalDock => (DataContext as MainWindowViewModel)?.IsVerticalDock == true;

    private void OnPinnedPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;
        var button = source.FindAncestorOfType<Button>(includeSelf: true);
        if (button is null) return;
        int index = IndexOfPinnedButton(button);
        if (index < 0) return;
        _lastPressedPinned = button;
        // The Settings gear is pinned to the end and not draggable.
        if (button.DataContext is DockItemViewModel { Item: DockSettingsItemModel }) return;
        _reorderSourceIndex = index;
        _reorderPressPoint = e.GetPosition(PinnedItems);
        _reorderInProgress = false;
    }

    private async void OnPinnedPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_reorderSourceIndex < 0 || _reorderInProgress) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsLocked) return;

        var current = e.GetPosition(PinnedItems);
        if (Math.Abs(current.X - _reorderPressPoint.X) < ReorderDragThresholdPixels &&
            Math.Abs(current.Y - _reorderPressPoint.Y) < ReorderDragThresholdPixels)
            return;

        // Past the threshold: this is a reorder, not a click. Dismiss any
        // window preview so it does not float over the drag.
        _reorderInProgress = true;
        HidePreview();
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(_reorderSourceIndex.ToString()));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        _reorderInProgress = false;
        _reorderSourceIndex = -1;
        HideDropIndicator();
    }

    private void OnPinnedPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _reorderSourceIndex = -1;
        _reorderInProgress = false;
    }

    private void OnPinnedPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // DoDragDropAsync itself takes the capture; only reset when no drag
        // is running, otherwise the source index would be lost mid-drag.
        if (_reorderInProgress) return;
        _reorderSourceIndex = -1;
        HideDropIndicator();
    }

    private void OnPinnedDragOver(object? sender, DragEventArgs e)
    {
        // An Explorer drag (files) is handled by the dock-level handler, which
        // decides between "pin here" and "open with this app". Let it bubble.
        if (!_reorderInProgress && e.DataTransfer.Contains(DataFormat.File))
            return;

        if (!_reorderInProgress || !e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        ShowDropIndicatorAt(e.GetPosition(PinnedItems));
        e.Handled = true;
    }

    private void OnPinnedDragLeave(object? sender, DragEventArgs e) => HideDropIndicator();

    private void OnPinnedDrop(object? sender, DragEventArgs e)
    {
        // File drops belong to the dock-level handler (pin / open-with).
        if (!_reorderInProgress && e.DataTransfer.Contains(DataFormat.File))
            return;

        HideDropIndicator();
        if (!_reorderInProgress) return;
        string? sourceText = e.DataTransfer.TryGetText();
        if (sourceText is null || !int.TryParse(sourceText, out int fromIndex)) return;

        var (gapIndex, _) = ResolvePinnedDropGap(e.GetPosition(PinnedItems));
        (DataContext as MainWindowViewModel)?.MoveItem(fromIndex, gapIndex);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    // --- Explorer drag-and-drop: pin files, or open them with a pinned app ---

    /// <summary>
    /// The program icon a file drag is currently hovering over, if any. When
    /// set, dropping opens the files with that program; otherwise the files
    /// are pinned at the indicated gap.
    /// </summary>
    private Button? _fileDropTarget;

    private void OnDockDragOver(object? sender, DragEventArgs e)
    {
        // Internal icon reorder has its own handler on the items control.
        if (_reorderInProgress) return;
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.Handled = true;

        // Over a pinned *program* icon → "open with"; anywhere else → pin.
        var overButton = FileDropButtonAt(e);
        SetFileDropTarget(overButton);

        if (overButton != null)
        {
            e.DragEffects = DragDropEffects.Copy;
            HideDropIndicator();
        }
        else
        {
            e.DragEffects = DragDropEffects.Link; // shows the "pin here" cursor
            ShowDropIndicatorAt(e.GetPosition(PinnedItems));
        }
    }

    private void OnDockDragLeave(object? sender, DragEventArgs e)
    {
        HideDropIndicator();
        SetFileDropTarget(null);
    }

    private void OnDockDrop(object? sender, DragEventArgs e)
    {
        if (_reorderInProgress) return;
        HideDropIndicator();
        var target = _fileDropTarget;
        SetFileDropTarget(null);

        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        if (DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;

        var paths = ExtractFilePaths(e);
        if (paths.Count == 0) return;

        // A dropped theme file applies the theme instead of being pinned —
        // pinning a .mbtheme as a launchable shortcut is never what was meant.
        if (paths.Count == 1 && TryApplyDroppedTheme(paths[0]))
        {
            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        if (target?.DataContext is DockItemViewModel itemVm && vm.OpenFilesWith(itemVm, paths))
        {
            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        // Locked: files still open with the app they were dropped on (above),
        // but nothing new gets pinned.
        if (IsLocked) { e.DragEffects = DragDropEffects.None; return; }

        var (gapIndex, _) = ResolvePinnedDropGap(e.GetPosition(PinnedItems));
        vm.PinDroppedPaths(paths, gapIndex);
        e.DragEffects = DragDropEffects.Link;
    }

    /// <summary>
    /// Applies a dropped <c>.mbtheme</c>. Returns false for anything else, so
    /// the normal pin/open-with handling continues. The file is validated by
    /// <see cref="ThemeFile.TryParse"/> before anything is changed, so a
    /// corrupt or unrelated file leaves the dock exactly as it was.
    /// </summary>
    private bool TryApplyDroppedTheme(string path)
    {
        if (_appServices == null) return false;
        if (!path.EndsWith(ThemeFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;

        AppearancePreset? preset = null;
        try
        {
            if (System.IO.File.Exists(path))
                preset = ThemeFile.TryParse(System.IO.File.ReadAllText(path));
        }
        catch { /* unreadable file: fall through and let it be pinned */ }

        if (preset == null) return false;

        _appServices.AppearanceService.SaveImportedPreset(preset);
        _appServices.AppearanceService.ApplyPreset(preset);
        if (DataContext is MainWindowViewModel mvm) mvm.RefreshAllDocks();
        App.RefreshWidgetAppearance();
        return true;
    }

    /// <summary>Local paths carried by an Explorer drag, files and folders alike.</summary>
    private static List<string> ExtractFilePaths(DragEventArgs e)
    {
        var paths = new List<string>();
        foreach (var item in e.DataTransfer.Items)
        {
            var storage = item.TryGetFile();
            // IStorageItem.Path is a URI; local drops always carry a file://
            // one, so LocalPath is the real Windows path.
            if (storage?.Path is { IsAbsoluteUri: true } uri && uri.IsFile)
                paths.Add(uri.LocalPath);
        }
        return paths;
    }

    /// <summary>
    /// The pinned *program* button under the pointer, or null. Only program
    /// items accept "open with" drops — folders, modules and the gear don't.
    /// </summary>
    private Button? FileDropButtonAt(DragEventArgs e)
    {
        var pos = e.GetPosition(PinnedItems);
        foreach (var cell in RealizedCells())
        {
            if (!cell.Bounds.Contains(pos)) continue;
            if (PinnedItems.ContainerFromIndex(cell.Index) is not Control container) return null;
            var button = container as Button ?? container.GetVisualDescendants().OfType<Button>().FirstOrDefault();
            if (button?.DataContext is DockItemViewModel { Item: DockProgramItemModel })
                return button;
            return null;
        }
        return null;
    }

    /// <summary>Highlights the icon that would receive an "open with" drop.</summary>
    private void SetFileDropTarget(Button? button)
    {
        if (ReferenceEquals(_fileDropTarget, button)) return;
        if (_fileDropTarget != null)
            _fileDropTarget.RenderTransform = null;
        _fileDropTarget = button;
        if (_fileDropTarget != null)
            _fileDropTarget.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1.3)");
    }

    private int IndexOfPinnedButton(Button button)
    {
        if (button.DataContext is not DockItemViewModel vm) return -1;
        if (DataContext is not MainWindowViewModel mainVm) return -1;
        return mainVm.Items.IndexOf(vm);
    }

    /// <summary>
    /// A realized pinned item's cell rectangle, in PinnedItems coordinates.
    /// </summary>
    private sealed record Cell(int Index, Rect Bounds);

    private List<Cell> RealizedCells()
    {
        var cells = new List<Cell>();
        foreach (var container in PinnedItems.GetRealizedContainers())
        {
            if (container is not Control c) continue;
            int index = PinnedItems.IndexFromContainer(c);
            if (index < 0) continue;
            if (c.TranslatePoint(new Point(0, 0), PinnedItems) is not Point topLeft) continue;
            if (c.Bounds.Width <= 0 || c.Bounds.Height <= 0) continue;
            cells.Add(new Cell(index, new Rect(topLeft, c.Bounds.Size)));
        }
        cells.Sort((a, b) => a.Index.CompareTo(b.Index));
        return cells;
    }

    /// <summary>
    /// Maps a pointer position inside the pinned ItemsControl to a gap index
    /// (0..Count) plus the on-screen line where the gap lies. Items are laid
    /// out in a UniformGrid (possibly several rows/columns), so the nearest
    /// cell is found by distance and the drop goes before or after it along
    /// the dock's main axis. Works for 1 line too.
    /// </summary>
    private (int GapIndex, Rect GapLine) ResolvePinnedDropGap(Point position)
    {
        var cells = RealizedCells();
        int count = (DataContext as MainWindowViewModel)?.Items.Count ?? 0;
        if (cells.Count == 0 || count == 0) return (0, new Rect(0, 0, 2, PinnedItems.Bounds.Height));

        bool vertical = IsVerticalDock;
        Cell nearest = cells[0];
        double best = double.MaxValue;
        foreach (var cell in cells)
        {
            var cx = Math.Clamp(position.X, cell.Bounds.Left, cell.Bounds.Right);
            var cy = Math.Clamp(position.Y, cell.Bounds.Top, cell.Bounds.Bottom);
            double d = (cx - position.X) * (cx - position.X) + (cy - position.Y) * (cy - position.Y);
            if (d < best) { best = d; nearest = cell; }
        }

        var b = nearest.Bounds;
        bool after = vertical ? position.Y > b.Center.Y : position.X > b.Center.X;
        int gap = Math.Min(nearest.Index + (after ? 1 : 0), count);
        // Never offer a slot after the Settings gear (it is kept last).
        int lastMovable = LastMovableIndex();
        if (gap > lastMovable + 1)
        {
            gap = lastMovable + 1;
            var lastCell = cells.FirstOrDefault(c => c.Index == lastMovable);
            if (lastCell != null) { b = lastCell.Bounds; after = true; }
        }
        Rect line = vertical
            ? new Rect(b.Left, (after ? b.Bottom : b.Top) - 1, b.Width, 2)
            : new Rect((after ? b.Right : b.Left) - 1, b.Top, 2, b.Height);
        return (gap, line);
    }

    /// <summary>Index of the last item that can be reordered (everything before the Settings gear).</summary>
    private int LastMovableIndex()
    {
        if (DataContext is not MainWindowViewModel vm) return -1;
        for (int i = vm.Items.Count - 1; i >= 0; i--)
            if (vm.Items[i].Item is not DockSettingsItemModel) return i;
        return -1;
    }

    private void ShowDropIndicatorAt(Point position)
    {
        var (_, line) = ResolvePinnedDropGap(position);
        DropIndicator.Width = line.Width;
        DropIndicator.Height = line.Height;
        DropIndicator.Margin = new Thickness(Math.Max(0, line.X), Math.Max(0, line.Y), 0, 0);
        DropIndicator.IsVisible = true;
    }

    private void HideDropIndicator() => DropIndicator.IsVisible = false;

    /// <summary>Scroll over a running program's icon to step through its windows.</summary>
    private void OnItemPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Button { DataContext: DockItemViewModel { Item: DockProgramItemModel item } } || _appServices == null) return;
        int direction = e.Delta.Y > 0 ? -1 : e.Delta.Y < 0 ? 1 : 0;
        if (direction == 0) return;
        e.Handled = true;
        HidePreview();
        var svc = _appServices.WindowPreviewService;
        Task.Run(() => svc.CycleWindows(item, direction));
    }

    // --- Right-click context menus: pin / unpin ---

    private void OnPinnedItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not DockItemViewModel vm) return;
        if (DataContext is not MainWindowViewModel mainVm) return;
        e.Handled = true;

        HidePreview();
        var loc = _appServices.LocalizationService;
        var menu = new ContextMenu();

        // Taskbar-style: the app's jump list tasks (New window, profiles...)
        // lead the menu. Alt+right-click shows only those, for people who
        // want the app's menu without the dock's housekeeping under it.
        if (vm.Item is DockProgramItemModel program)
        {
            bool added = AddJumpListItems(menu, program.ExecutablePath);
            if (User32.IsAltDown())
            {
                if (!added)
                    menu.Items.Add(new MenuItem { Header = loc.Text("dock.context.noJumpList"), IsEnabled = false });
                OpenDismissableMenu(menu, button);
                return;
            }
            if (added) menu.Items.Add(new Separator());
        }

        if (vm.Item is DockSeparatorItemModel separator)
        {
            // A divider has no icon and nothing to launch. It can be widened
            // into a spacer (blank room on either side of the line, or with
            // the line hidden), duplicated, or removed.
            var spacingMenu = new MenuItem { Header = loc.Text("dock.context.separatorSpacing") };
            foreach (double preset in DockSeparatorItemModel.SpacingPresets)
            {
                double captured = preset;
                var option = new MenuItem
                {
                    Header = SpacingCaption(loc, preset),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = Math.Abs(separator.Spacing - preset) < 0.001,
                };
                option.Click += (_, _) => mainVm.SetSeparatorSpacing(vm, captured, separator.HideLine);
                spacingMenu.Items.Add(option);
            }
            menu.Items.Add(spacingMenu);

            var hideLine = new MenuItem
            {
                Header = loc.Text("dock.context.separatorHideLine"),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = separator.HideLine,
            };
            hideLine.Click += (_, _) => mainVm.SetSeparatorSpacing(vm, separator.Spacing, !separator.HideLine);
            menu.Items.Add(hideLine);

            // Colour: a palette of quick picks plus "Use dock colour" to go
            // back to the dock-wide Separator colour from Settings.
            var colorMenu = new MenuItem { Header = loc.Text("dock.context.separatorColor") };
            var useDefault = new MenuItem
            {
                Header = loc.Text("dock.context.separatorColor.default"),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = separator.Color == null,
            };
            useDefault.Click += (_, _) => mainVm.SetSeparatorColor(vm, null);
            colorMenu.Items.Add(useDefault);
            colorMenu.Items.Add(new Separator());
            foreach (string hex in SeparatorColors.MenuPalette)
            {
                string captured = hex;
                var (a, r, g, b) = SeparatorColors.ToArgb(hex);
                // Drawn at its real strength over mid-grey, so "default"
                // (faint white), bright white and dark read differently.
                var swatch = new Border
                {
                    Width = 28, Height = 12, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
                    BorderThickness = new Thickness(1),
                    Child = new Border
                    {
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                    },
                };
                var option = new MenuItem
                {
                    Header = swatch,
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = string.Equals(separator.Color, captured, StringComparison.OrdinalIgnoreCase),
                };
                // UIA/automation name: the hex, since the header is a swatch.
                Avalonia.Automation.AutomationProperties.SetName(option, captured);
                option.Click += (_, _) => mainVm.SetSeparatorColor(vm, captured);
                colorMenu.Items.Add(option);
            }
            menu.Items.Add(colorMenu);

            menu.Items.Add(new Separator());

            var removeSep = new MenuItem { Header = loc.Text("dock.context.removeSeparator") };
            removeSep.Click += (_, _) => mainVm.RemoveSeparator(vm);
            menu.Items.Add(removeSep);

            var addAfterSep = new MenuItem { Header = loc.Text("dock.context.addSeparator") };
            addAfterSep.Click += (_, _) => mainVm.AddSeparatorAfter(vm);
            menu.Items.Add(addAfterSep);

            OpenDismissableMenu(menu, button);
            return;
        }

        // Any real item can carry a custom icon.
        var changeIcon = new MenuItem { Header = loc.Text("dock.context.changeIcon") };
        changeIcon.Click += async (_, _) => await PickCustomIconAsync(mainVm, vm);
        menu.Items.Add(changeIcon);

        if (vm.Item.CustomIcon != null)
        {
            var resetIcon = new MenuItem { Header = loc.Text("dock.context.resetIcon") };
            resetIcon.Click += (_, _) => mainVm.SetCustomIcon(vm, null);
            menu.Items.Add(resetIcon);
        }

        // Insert a divider right after this icon.
        var addSeparator = new MenuItem { Header = loc.Text("dock.context.addSeparator") };
        addSeparator.Click += (_, _) => mainVm.AddSeparatorAfter(vm);
        menu.Items.Add(addSeparator);

        // Only program items are unpinnable from the dock; the Settings item
        // and Windows modules are managed from the Settings window.
        if (vm.Item is DockProgramItemModel programItem)
        {
            menu.Items.Add(new Separator());
            AddHideForAppItem(menu, programItem.ExecutablePath);
            var unpin = new MenuItem { Header = loc.Text("dock.context.unpin") };
            unpin.Click += (_, _) => mainVm.UnpinItem(vm);
            menu.Items.Add(unpin);
            AddCloseWindowsItem(menu, programItem.ExecutablePath);
        }

        // The gear carries the way out. Quitting was previously only reachable
        // from the tray icon, which users did not find — the launch thread had
        // someone convinced the app could not be closed at all.
        if (vm.Item is DockSettingsItemModel)
        {
            menu.Items.Add(new Separator());

            var lockItem = new MenuItem
            {
                Header = loc.Text("dock.context.lockDock"),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = IsLocked,
            };
            lockItem.Click += (_, _) =>
                _appServices.AppearanceService.SetLockDock(!_appServices.AppearanceService.GetLockDock());
            menu.Items.Add(lockItem);

            // Same wiring as the Settings toggle: persist, re-apply on this
            // window (via LayerRefreshAction), re-reserve the edge, and let
            // the mirrors pick it up.
            var autoHideItem = new MenuItem
            {
                Header = loc.Text("dock.context.autoHide"),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _appServices.AppearanceService.GetAutoHide(),
            };
            autoHideItem.Click += (_, _) =>
            {
                _appServices.AppearanceService.SetAutoHide(!_appServices.AppearanceService.GetAutoHide());
                App.PrimaryViewModel?.UpdateDockUI();
                App.SyncMirrorDocks();
                App.ApplyEdgeReservation();
            };
            menu.Items.Add(autoHideItem);

            // Centring only means anything for a freely-dragged dock. In
            // STATIC mode the anchors already place it and the entry would do
            // nothing visible, so it is hidden there rather than shown inert.
            if (_appServices.PositioningService.IsDynamicPositioning())
            {
                var center = new MenuItem { Header = loc.Text("dock.context.centerDock") };
                // Routed through App so a mirror's menu centres the primary
                // (mirrors derive their position from it) rather than trying
                // to persist its own off-screen coordinates.
                center.Click += (_, _) => App.CenterPrimaryDock();
                menu.Items.Add(center);
            }

            var quit = new MenuItem { Header = loc.Text("dock.context.quit") };
            quit.Click += (_, _) => ConfirmAndQuit();
            menu.Items.Add(quit);
        }

        OpenDismissableMenu(menu, button);
    }

    /// <summary>
    /// Centres the dock along the screen edge it sits on and persists it, so
    /// a dock nudged off-centre by a drag can be put back without aiming.
    ///
    /// <b>Primary dock only</b> — the menu entry on a mirror routes here via
    /// <see cref="App.CenterPrimaryDock"/>. A mirror has no position of its
    /// own: it derives one from the primary's saved position expressed as a
    /// fraction of the primary's screen. Centring a mirror directly would
    /// persist that mirror's screen coordinates (negative, on a monitor left
    /// of the primary) as the *primary's* position, and the next mirror
    /// refresh would clamp the resulting fraction to 0 and slam every mirror
    /// against its left edge.
    /// </summary>
    public void CenterDock()
    {
        if (_appServices == null || IsMirror) return;

        // Centre the visible bar, not the padded window.
        var inset = MagnifyInset();
        var rect = VisibleBarScreenRect();
        var work = ScreenGeometry.WorkAreaAt(
            new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        var bounds = new ScreenBounds(work.X, work.Y, work.Width, work.Height);

        var (bx, by) = DockPositioningService.CenterAlongEdge(
            bounds, rect.X, rect.Y, rect.Width, rect.Height,
            _appServices.AppearanceService.GetVerticalDock());
        var (x, y) = (bx - inset.X, by - inset.Y);

        SetScreenPosition((int)x, (int)y);
        // Persist the bar position, matching how it is read back.
        _appServices.DockService.SetDockPosition((int)bx, (int)by);
        App.RepositionMirrorDocks();
        ApplyEdgeReservation();
        // Auto-hide caches where the dock rests; without this the next hide
        // would slide back to the old off-centre spot.
        _autoHide?.OnLayoutChanged();
    }

    /// <summary>
    /// Asks before quitting, then goes through the one shutdown path that also
    /// restores the Windows taskbar — leaving it hidden with the dock gone
    /// would look like the desktop had broken.
    /// </summary>
    private void ConfirmAndQuit()
    {
        if (_appServices == null) return;
        var loc = _appServices.LocalizationService;
        var answer = System.Windows.Forms.MessageBox.Show(
            loc.Text("dialog.quit.message"),
            loc.Text("dialog.quit.title"),
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question,
            System.Windows.Forms.MessageBoxDefaultButton.Button1);
        if (answer == System.Windows.Forms.DialogResult.Yes)
            App.RequestShutdown();
    }

    /// <summary>
    /// Caption for a spacer preset: "None" for a plain hairline, otherwise
    /// the width as a fraction of an icon ("½ icon", "1 icon").
    /// </summary>
    private static string SpacingCaption(LocalizationService loc, double fraction)
    {
        if (fraction <= 0) return loc.Text("dock.context.separatorSpacing.none");
        string amount = fraction switch
        {
            0.25 => "¼",
            0.5 => "½",
            0.75 => "¾",
            _ => fraction.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
        };
        return loc.Text("dock.context.separatorSpacing.icon", amount);
    }

    /// <summary>
    /// Asks for an icon file and applies it to the item. The dock window is
    /// WS_EX_NOACTIVATE, so the picker is opened from the Settings window when
    /// one is available and falls back to this window otherwise.
    /// </summary>
    private async Task PickCustomIconAsync(MainWindowViewModel mainVm, DockItemViewModel item)
    {
        if (_appServices == null) return;
        var loc = _appServices.LocalizationService;
        var files = await StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = loc.Text("dialog.iconChooser.title"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType(loc.Text("dialog.iconChooser.filter"))
                    {
                        Patterns = new[] { "*.png", "*.ico", "*.exe", "*.dll", "*.jpg", "*.jpeg", "*.bmp" }
                    },
                }
            });
        if (files.Count == 0) return;
        mainVm.SetCustomIcon(item, files[0].Path.LocalPath);
    }

    /// <summary>
    /// Opens a dock context menu and arranges for it to be dismissed when the
    /// user clicks anywhere outside it - see <see cref="DismissableMenu"/>.
    /// </summary>
    private void OpenDismissableMenu(ContextMenu menu, Control anchor) => _menu.Open(menu, anchor);

    private readonly DismissableMenu _menu = new();

    private void DisposeMenuDismisser() => _menu.Dispose();

    private void OnRunningAppContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel vm) return;
        if (DataContext is not MainWindowViewModel mainVm) return;
        e.Handled = true;

        HidePreview();
        var loc = _appServices.LocalizationService;
        var menu = new ContextMenu();
        bool added = AddJumpListItems(menu, vm.ExecutablePath);
        if (User32.IsAltDown())
        {
            if (!added)
                menu.Items.Add(new MenuItem { Header = loc.Text("dock.context.noJumpList"), IsEnabled = false });
            OpenDismissableMenu(menu, button);
            return;
        }
        if (added) menu.Items.Add(new Separator());
        var pin = new MenuItem { Header = loc.Text("dock.context.pin") };
        pin.Click += (_, _) => mainVm.PinRunningApp(vm);
        menu.Items.Add(pin);
        AddHideForAppItem(menu, vm.ExecutablePath);
        AddCloseWindowsItem(menu, vm.ExecutablePath);
        OpenDismissableMenu(menu, button);
    }

    /// <summary>
    /// Taskbar-style last entry: "Close window" (one open) or "Close all
    /// windows" (several). Omitted when the program has no open window. Sends
    /// WM_CLOSE like the taskbar does, so apps can still ask to save.
    /// </summary>
    private void AddCloseWindowsItem(ContextMenu menu, string? executablePath)
    {
        if (_appServices == null || string.IsNullOrEmpty(executablePath)) return;
        // Never offer to close the dock's own windows (Settings, widgets).
        string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe";
        if (string.Equals(DockAppearanceService.NormalizeExeName(executablePath), self, StringComparison.OrdinalIgnoreCase)) return;

        var previews = _appServices.WindowPreviewService;
        int count = previews.CountOpenWindows(executablePath);
        if (count == 0) return;

        var loc = _appServices.LocalizationService;
        menu.Items.Add(new Separator());
        var close = new MenuItem
        {
            Header = loc.Text(count == 1 ? "dock.context.closeWindow" : "dock.context.closeAllWindows"),
        };
        close.Click += (_, _) =>
        {
            HidePreview();
            Task.Run(() => previews.CloseAll(executablePath));
        };
        menu.Items.Add(close);
    }

    /// <summary>
    /// Checkable "Hide dock while this app is focused" entry. Toggles the
    /// per-app rule for the item's executable (matched by file name).
    /// </summary>
    private void AddHideForAppItem(ContextMenu menu, string? executablePath)
    {
        if (_appServices == null || string.IsNullOrEmpty(executablePath)) return;
        // The poll ignores our own process, so a rule for the dock itself
        // could never fire; don't offer one.
        string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe";
        if (string.Equals(DockAppearanceService.NormalizeExeName(executablePath), self, StringComparison.OrdinalIgnoreCase)) return;
        var appearance = _appServices.AppearanceService;
        var item = new MenuItem
        {
            Header = _appServices.LocalizationService.Text("dock.context.hideForApp"),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = appearance.IsHideForApp(executablePath),
        };
        item.Click += (_, _) =>
        {
            if (appearance.IsHideForApp(executablePath)) appearance.RemoveHideForApp(executablePath);
            else appearance.AddHideForApp(executablePath);
        };
        menu.Items.Add(item);
    }

    /// <summary>
    /// Appends the app's jump list (the taskbar's right-click "Tasks" and any
    /// custom groups) to <paramref name="menu"/>. Returns false when the app
    /// has none. Named groups get a disabled caption row, as the taskbar does.
    /// </summary>
    private bool AddJumpListItems(ContextMenu menu, string executablePath)
    {
        if (_appServices == null) return false;
        var categories = _appServices.JumpListGateway.GetCategories(executablePath);
        if (categories.Count == 0) return false;

        bool first = true;
        foreach (var category in categories)
        {
            if (!first) menu.Items.Add(new Separator());
            first = false;
            if (category.Name != null)
                menu.Items.Add(new MenuItem { Header = category.Name, IsEnabled = false });
            foreach (var entry in category.Entries)
            {
                var item = new MenuItem { Header = entry.Title };
                if (entry.Description != null) ToolTip.SetTip(item, entry.Description);
                var captured = entry;
                item.Click += (_, _) =>
                    _appServices.ItemActionService.LaunchCommand(captured.ExecutablePath, captured.Title, captured.Arguments);
                menu.Items.Add(item);
            }
        }
        return true;
    }

    /// <summary>
    /// PositionChanged fires for every move — including each WM_MOVE delivered
    /// while the OS move-drag loop runs. The drag blocks the UI thread, so a
    /// DispatcherTimer restart here only ticks after the drag ends, persisting
    /// the final position exactly once instead of on every pixel of the drag.
    /// </summary>
    private void OnDockPositionChanged(object? sender, PixelPointEventArgs e)
    {
        RefreshTooltipPlacement();
        if (_appServices == null || IsMirror) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        _positionPersistTimer.Stop();
        _positionPersistTimer.Start();
    }

    /// <summary>
    /// Persisted dock position, in <i>visible bar</i> coordinates.
    ///
    /// The saved position is read back as a bar position (see
    /// <see cref="ApplyDockPosition"/>), so it has to be written as one too.
    /// Storing the raw window position would shift the dock by the headroom
    /// on the next launch, and again each time the headroom changed size.
    /// </summary>
    private (int X, int Y) BarPositionForPersist()
    {
        var (x, y) = GetScreenPosition();
        var inset = MagnifyInset();
        return (x + inset.X, y + inset.Y);
    }

    private void PersistDockPosition()
    {
        if (_appServices == null || IsMirror) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        // Auto-hide moves the window itself; those moves are not user drags.
        if (_autoHide is { IsEnabled: true }) return;
        var (x, y) = GetScreenPosition();
        if (_appServices.AppearanceService.GetEdgeSnapping())
        {
            // Snap what the user sees, then convert back to a window position.
            var inset = MagnifyInset();
            var rect = VisibleBarScreenRect();
            var centre = new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            // Snap against the FULL monitor while we reserve an edge: the work
            // area already excludes our own strip, so snapping to it would park
            // the dock a dock-height above the edge, which then reads as "not
            // docked" (beyond DockReservation.EdgeTolerance) and silently drops
            // the reservation. Each snap would walk the dock further inward.
            bool reserving = _appServices.AppearanceService.GetReserveScreenEdge()
                             && !_appServices.AppearanceService.GetAutoHide();
            var work = reserving
                ? ScreenGeometry.MonitorAreaAt(centre)
                : ScreenGeometry.WorkAreaAt(centre);
            var snapped = EdgeSnapper.Snap(rect, work, _appServices.AppearanceService.GetEdgeSnapMargin());
            var target = new PixelPoint(snapped.X - inset.X, snapped.Y - inset.Y);
            if (target.X != x || target.Y != y)
            {
                SetScreenPosition(target.X, target.Y);
                (x, y) = (target.X, target.Y);
            }
        }
        var (px, py) = BarPositionForPersist();
        _appServices.DockService.SetDockPosition(px, py);
        App.RepositionMirrorDocks();
        // Dragged to (or away from) an edge: re-evaluate what to reserve.
        ApplyEdgeReservation();
    }

    private void UpdateStatus(string status)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusText = status;
    }

    /// <summary>
    /// Opens the settings window on this dock's monitor. The callbacks always
    /// target the <b>primary</b> dock: its refresh cascades to every mirror
    /// (see <see cref="MainWindowViewModel.UpdateDockUI"/>), while a mirror's
    /// own refresh deliberately doesn't recurse — so Settings opened from a
    /// secondary monitor's gear used to update that mirror only (e.g. vertical
    /// layout applied to the mirror, primary left horizontal). The positioning
    /// callback likewise must persist the primary's position, never a mirror's.
    /// </summary>
    private void OpenSettings(MainWindowViewModel vm)
    {
        if (_appServices == null) return;
        SettingsWindow.Open(
            _appServices,
            this,
            dockRefreshAction: vm.RefreshAllDocks,
            positioningModeChangeAction: IsMirror
                ? App.HandlePositioningModeChange
                : mode => HandlePositioningModeChange(mode)
        );
    }

    /// <summary></summary>
    private void HandlePositioningModeChange(DockPositioningMode mode)
    {
        if (_appServices == null) return;
        var currentMode = _appServices.PositioningService.GetPositioningMode();
        if (currentMode == DockPositioningMode.STATIC && mode == DockPositioningMode.DYNAMIC)
        {
            var (x, y) = BarPositionForPersist();
            _appServices.DockService.SetDockPosition(x, y);
        App.RepositionMirrorDocks();
        }
        _appServices.PositioningService.SetPositioningMode(mode);
    }

    /// <summary>
    /// Current position of the <i>visible bar</i>, for callers outside the
    /// window (App). Bar coordinates, not window coordinates: the window
    /// carries transparent headroom, and every persisted/derived position in
    /// the app is expressed against what the user can see.
    /// </summary>
    public (int X, int Y) CurrentScreenPosition => BarPositionForPersist();

    /// <summary>Mirror docks: recompute the anchored position (primary moved or layout changed).</summary>
    public void ReapplyPosition() => ApplyDockPosition(force: true);

    /// <summary>Tooltips open on the side of the dock facing the screen centre so they never cover the icon.</summary>
    private void RefreshTooltipPlacement()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var rect = VisibleBarScreenRect();
            var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            bool restedLeft = vm.VerticalRestsOnLeft;
            vm.UpdateTooltipPlacement(rect.X, rect.Y, rect.Width, rect.Height, work.X, work.Y, work.Right, work.Bottom);
            // Dragged across the screen's centre line: the magnification
            // origin and the growth headroom swap sides with the resting edge.
            if (vm.VerticalRestsOnLeft != restedLeft) SyncPinnedPanel();
        }
        catch { }
    }

    /// <summary>Fullscreen auto-hide: show/hide the native window without changing layering.</summary>
    public void SetNativeVisible(bool visible) => _dockBehavior?.SetNativeVisible(visible);

    /// <summary>
    /// Hotkey path: if auto-hide has the dock slid away, slide it back and
    /// return true; otherwise return false so the caller can hide it instead.
    /// </summary>
    public bool RevealIfAutoHidden()
    {
        if (_autoHide is not { IsEnabled: true, IsHidden: true }) return false;
        _autoHide.OnPointerEntered();
        return true;
    }

    private bool IsLocked => _appServices?.AppearanceService.GetLockDock() == true;

    /// <summary>Re-reads the always-on-top setting and switches the window layer live.</summary>
    public void ApplyAlwaysOnTop()
    {
        if (_appServices == null || _dockBehavior == null) return;
        _dockBehavior.SetAlwaysOnTop(_appServices.AppearanceService.GetAlwaysOnTop());
    }

    /// <summary>
    /// #4 Appbar reservation: keeps maximized windows off the dock's screen
    /// edge. Re-evaluated on every move, resize and settings change, because
    /// the strip to reserve is derived from where the dock actually sits.
    ///
    /// Every dock window reserves its <i>own</i> monitor's edge, mirrors
    /// included — the strip is derived from the monitor under that dock, and
    /// each window owns a separate <see cref="AppBarReservation"/> (the shell
    /// keys an appbar on its HWND, so they do not contend).
    ///
    /// Deliberately skipped in two cases:
    /// <list type="bullet">
    /// <item><b>Auto-hide on</b> — a hidden dock that still reserved its edge
    /// would leave a permanent dead band with nothing visible in it.</item>
    /// <item><b>Not docked to an edge</b> — a floating dock reserves nothing;
    /// <see cref="DockReservation.ResolveEdge"/> returns None and the
    /// registration is released.</item>
    /// </list>
    /// </summary>
    public void ApplyEdgeReservation()
    {
        if (_appServices == null) return;
        // Coalesce: a settings change runs several layout passes (bar size,
        // then headroom), each raising SizeChanged. Every ABM_SETPOS makes the
        // shell re-lay out its appbars and briefly show the secondary-monitor
        // taskbar, so the strip is updated once, after the burst settles.
        _reservationDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background,
            (_, _) => { _reservationDebounce!.Stop(); ApplyEdgeReservationNow(); });
        _reservationDebounce.Stop();
        _reservationDebounce.Start();
    }

    private DispatcherTimer? _reservationDebounce;

    private void ApplyEdgeReservationNow()
    {
        if (_appServices == null) return;

        bool wanted = _appServices.AppearanceService.GetReserveScreenEdge()
                      && !_appServices.AppearanceService.GetAutoHide();
        if (!wanted)
        {
            _reservation?.Clear();
            return;
        }

        // The painted bar, not the window rect: SizeToContent plus the bounce
        // and magnification headroom mean the window is larger than what the
        // user sees, and the reserved strip has to match the painted bar.
        // The cross-axis headroom is excluded on purpose - magnified icons
        // are meant to overlap the window above, not push it up.
        var rect = PaintedBarScreenRect();
        var centre = new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

        // Measured against the full monitor, never the work area: the work
        // area already excludes our own strip, so deriving the reservation
        // from it would shrink the dock's band a little more on every pass.
        var monitor = ScreenGeometry.MonitorAreaAt(centre);
        var bounds = new ScreenBounds(monitor.X, monitor.Y, monitor.Width, monitor.Height);

        var edge = DockReservation.ResolveEdge(rect.X, rect.Y, rect.Width, rect.Height, bounds);
        var strip = DockReservation.ComputeReservation(edge, rect.X, rect.Y, rect.Width, rect.Height, bounds);

        _reservation ??= new AppBarReservation();
        _reservation.Apply(edge, strip);
    }

    protected override void OnClosed(EventArgs e)
    {
        PositionChanged -= OnDockPositionChanged;
        _positionPersistTimer.Stop();
        _previewShowDelay.Stop();
        DisposeMenuDismisser();
        HidePreview();
        if (DataContext is MainWindowViewModel vm)
            vm.Shutdown();
        _autoHide?.Dispose();
        _autoHide = null;
        _reservationDebounce?.Stop();
        _reservationDebounce = null;
        // Releasing the appbar restores the work area. Must happen before the
        // process ends: a stale reservation leaves the user's screen shrunk
        // with no UI left to undo it.
        _reservation?.Dispose();
        _reservation = null;
        _folderStack?.Close();
        _folderStack = null;
        _dockBehavior?.Dispose();
        _dockBehavior = null;
        base.OnClosed(e);

        // The dock IS the application: closing it (WM_CLOSE from the shell,
        // Alt+F4, a task manager "End task") must end the process — otherwise
        // widget windows keep it alive with no dock to exit from, and a hidden
        // taskbar would stay hidden. Mirrors are disposable; only the primary
        // dock carries the process.
        if (!IsMirror) App.RequestShutdown();
    }
}