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
        _dockBehavior.Apply(_appServices?.AppearanceService.GetAlwaysOnTop() ?? false);

        // Initialize the dock ViewModel (loads items, starts indicator watcher).
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenSettingsAction = () => OpenSettings(vm);
            vm.RepositionAction = () => ApplyDockPosition();
            vm.LayerRefreshAction = () => { ApplyAlwaysOnTop(); ApplyAutoHideSetting(); ApplyBackdrop(); SyncPinnedPanel(); ApplyEdgeReservation(); };
            vm.ShowFolderStackAction = ShowFolderStack;
            vm.PreviewDismissAction = HidePreview;
            vm.Initialize();
        }

        ApplyDockPosition(force: true);

        _autoHide = new DockAutoHideController(
            behavior: () => _dockBehavior,
            size: () => ((int)Math.Round(Bounds.Width), (int)Math.Round(Bounds.Height)),
            restPosition: RestPosition,
            screenBounds: () =>
            {
                var sb = OwnScreenBounds();
                return ((int)sb.MinX, (int)sb.MinY, (int)sb.MaxX, (int)sb.MaxY);
            },
            blockHide: () => _previewPopup?.IsVisible == true || _folderStack?.IsVisible == true || _reorderInProgress);
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
        var (x, y) = ResolveOwnPosition(Width, Height);
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

    private ScreenBounds OwnScreenBounds()
    {
        var pos = _appServices!.PositioningService;
        if (IsMirror && pos.FindScreen(MirrorScreenId!) is { } s) return s.Bounds;
        return pos.GetPrimaryScreenBounds();
    }

    /// <summary>
    /// Where the dock sits when fully shown: the anchored position in STATIC
    /// mode, the persisted position in DYNAMIC mode. Auto-hide slides away
    /// from and back to this point.
    /// </summary>
    private (int X, int Y) RestPosition()
    {
        if (_appServices == null) return GetScreenPosition();
        var (x, y) = ResolveOwnPosition(Bounds.Width, Bounds.Height);
        return ((int)x, (int)y);
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

    private void OnDockSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RefreshTooltipPlacement();
        // SizeToContent means the bar's rect changed — re-clip the backdrop.
        UpdateBackdropRegion();
        if (IsMirror || _appServices?.PositioningService.IsDynamicPositioning() == false)
            ApplyDockPosition();
        // A taller/wider bar must reserve a correspondingly bigger strip.
        ApplyEdgeReservation();
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

        foreach (var (panel, host) in Panels(pinned, running))
        {
            panel.Lines = vm.DockLines;
            panel.IsVertical = vm.IsVerticalDock;
            panel.MagnifyScale = scale;
            // The per-item hover zoom must stand down while the panel is scaling.
            host.Classes.Set("magnified", scale > 1.0);
            if (scale <= 1.0) { panel.UpdateMagnification(null); ClearDividerTransform(); }
        }
        SyncRowContext(pinned, running);
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
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor());

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
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor());
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
        BeginMoveDrag(e);
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
        if (DataContext is MainWindowViewModel mvm) mvm.UpdateDockUI();
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

        if (vm.Item is DockSeparatorItemModel)
        {
            // A divider has no icon and nothing to launch; its only action is
            // to go away. Adding another one from here is still useful.
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
        if (vm.Item is DockProgramItemModel)
        {
            menu.Items.Add(new Separator());
            var unpin = new MenuItem { Header = loc.Text("dock.context.unpin") };
            unpin.Click += (_, _) => mainVm.UnpinItem(vm);
            menu.Items.Add(unpin);
        }

        // The gear carries the way out. Quitting was previously only reachable
        // from the tray icon, which users did not find — the launch thread had
        // someone convinced the app could not be closed at all.
        if (vm.Item is DockSettingsItemModel)
        {
            menu.Items.Add(new Separator());

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

        var rect = ScreenGeometry.WindowScreenRect(this);
        var work = ScreenGeometry.WorkAreaAt(
            new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        var bounds = new ScreenBounds(work.X, work.Y, work.Width, work.Height);

        var (x, y) = DockPositioningService.CenterAlongEdge(
            bounds, rect.X, rect.Y, rect.Width, rect.Height,
            _appServices.AppearanceService.GetVerticalDock());

        SetScreenPosition((int)x, (int)y);
        _appServices.DockService.SetDockPosition((int)x, (int)y);
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
    /// user clicks anywhere outside it.
    ///
    /// The dock window is WS_EX_NOACTIVATE, so it is never activated and never
    /// deactivated — the usual light-dismiss never fires and the menu would sit
    /// there after a click on the desktop or another app. A low-level mouse
    /// hook supplies the clicks the window cannot see; the hook only lives
    /// while a menu is open.
    /// </summary>
    private void OpenDismissableMenu(ContextMenu menu, Control anchor)
    {
        DisposeMenuDismisser();

        menu.Closed += (_, _) => DisposeMenuDismisser();
        menu.Open(anchor);

        // Arm after the popup exists, so its bounds can be hit-tested.
        Dispatcher.UIThread.Post(() =>
        {
            if (!menu.IsOpen) return;
            _openMenu = menu;
            var hook = new GlobalMouseHook((x, y) =>
                Dispatcher.UIThread.Post(() => OnGlobalClick(x, y)));
            if (hook.IsInstalled) _menuDismissHook = hook;
            else hook.Dispose(); // no hook: the menu still closes on selection
        }, DispatcherPriority.Background);
    }

    private GlobalMouseHook? _menuDismissHook;
    private ContextMenu? _openMenu;

    /// <summary>
    /// Closes the open context menu unless the click landed inside it. The
    /// popup is its own top-level window, so its screen rect comes from the
    /// PopupRoot rather than from the anchor control.
    /// </summary>
    private void OnGlobalClick(int screenX, int screenY)
    {
        if (_openMenu is not { IsOpen: true } menu)
        {
            DisposeMenuDismisser();
            return;
        }

        if (menu.GetVisualRoot() is Visual root)
        {
            var topLeft = root.PointToScreen(new Point(0, 0));
            var size = root.Bounds.Size;
            var rect = new PixelRect(topLeft,
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
            // Inside the menu: let Avalonia handle the selection itself.
            if (rect.Contains(new PixelPoint(screenX, screenY))) return;
        }

        menu.Close();
        DisposeMenuDismisser();
    }

    private void DisposeMenuDismisser()
    {
        _menuDismissHook?.Dispose();
        _menuDismissHook = null;
        _openMenu = null;
    }

    private void OnRunningAppContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel vm) return;
        if (DataContext is not MainWindowViewModel mainVm) return;
        e.Handled = true;

        HidePreview();
        var loc = _appServices.LocalizationService;
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = loc.Text("dock.context.pin") };
        pin.Click += (_, _) => mainVm.PinRunningApp(vm);
        menu.Items.Add(pin);
        OpenDismissableMenu(menu, button);
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

    private void PersistDockPosition()
    {
        if (_appServices == null || IsMirror) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        // Auto-hide moves the window itself; those moves are not user drags.
        if (_autoHide is { IsEnabled: true }) return;
        var (x, y) = GetScreenPosition();
        if (_appServices.AppearanceService.GetEdgeSnapping())
        {
            var rect = ScreenGeometry.WindowScreenRect(this);
            var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            var snapped = EdgeSnapper.Snap(rect, work, _appServices.AppearanceService.GetEdgeSnapMargin());
            if (snapped.X != x || snapped.Y != y)
            {
                SetScreenPosition(snapped.X, snapped.Y);
                (x, y) = (snapped.X, snapped.Y);
            }
        }
        _appServices.DockService.SetDockPosition(x, y);
        App.RepositionMirrorDocks();
        // Dragged to (or away from) an edge: re-evaluate what to reserve.
        ApplyEdgeReservation();
    }

    private void UpdateStatus(string status)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusText = status;
    }

    /// <summary>Opens the settings window. Port of App.java openSettingsWindow().</summary>
    private void OpenSettings(MainWindowViewModel vm)
    {
        if (_appServices == null) return;
        SettingsWindow.Open(
            _appServices,
            this,
            dockRefreshAction: vm.UpdateDockUI,
            positioningModeChangeAction: mode => HandlePositioningModeChange(mode)
        );
    }

    /// <summary>Port of App.java handlePositioningModeChange().</summary>
    private void HandlePositioningModeChange(DockPositioningMode mode)
    {
        if (_appServices == null) return;
        var currentMode = _appServices.PositioningService.GetPositioningMode();
        if (currentMode == DockPositioningMode.STATIC && mode == DockPositioningMode.DYNAMIC)
        {
            var (x, y) = GetScreenPosition();
            _appServices.DockService.SetDockPosition(x, y);
        App.RepositionMirrorDocks();
        }
        _appServices.PositioningService.SetPositioningMode(mode);
    }

    /// <summary>Current absolute screen position, for callers outside the window (App).</summary>
    public (int X, int Y) CurrentScreenPosition => GetScreenPosition();

    /// <summary>Mirror docks: recompute the anchored position (primary moved or layout changed).</summary>
    public void ReapplyPosition() => ApplyDockPosition(force: true);

    /// <summary>Tooltips open on the side of the dock facing the screen centre so they never cover the icon.</summary>
    private void RefreshTooltipPlacement()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var rect = ScreenGeometry.WindowScreenRect(this);
            var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            vm.UpdateTooltipPlacement(rect.X, rect.Y, rect.Width, rect.Height, work.X, work.Y, work.Right, work.Bottom);
        }
        catch { }
    }

    /// <summary>Fullscreen auto-hide: show/hide the native window without changing layering.</summary>
    public void SetNativeVisible(bool visible) => _dockBehavior?.SetNativeVisible(visible);

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
    /// Deliberately skipped in three cases:
    /// <list type="bullet">
    /// <item><b>Mirror docks</b> — only the primary dock reserves. One appbar
    /// per process is what the shell expects, and a secondary monitor's strip
    /// would fight the primary's for the same registration.</item>
    /// <item><b>Auto-hide on</b> — a hidden dock that still reserved its edge
    /// would leave a permanent dead band with nothing visible in it.</item>
    /// <item><b>Not docked to an edge</b> — a floating dock reserves nothing;
    /// <see cref="DockReservation.ResolveEdge"/> returns None and the
    /// registration is released.</item>
    /// </list>
    /// </summary>
    public void ApplyEdgeReservation()
    {
        if (IsMirror || _appServices == null) return;

        bool wanted = _appServices.AppearanceService.GetReserveScreenEdge()
                      && !_appServices.AppearanceService.GetAutoHide();
        if (!wanted)
        {
            _reservation?.Clear();
            return;
        }

        // The window rect, not Bounds: SizeToContent plus the bounce headroom
        // margin mean Bounds is the content, while the reserved strip has to
        // match what the user sees at the edge.
        var rect = ScreenGeometry.WindowScreenRect(this);
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

    /// <summary>
    /// Applies the configured backdrop (none / blur / acrylic) and clips the
    /// native window to the dock bar so the blur follows the rounded corners
    /// instead of filling the whole window rectangle (which includes the
    /// transparent bounce headroom).
    ///
    /// The backdrop is driven through Avalonia's <see cref="Window.TransparencyLevelHint"/>,
    /// *not* the legacy <c>SetWindowCompositionAttribute</c> accent policy.
    /// Avalonia creates this window with <c>WS_EX_NOREDIRECTIONBITMAP</c> and
    /// renders it through DirectComposition, and the accent policy paints into
    /// a window's redirection surface — which this window does not have. The
    /// accent call therefore *succeeds and draws nothing*: measured with
    /// Desktop Duplication, pixels behind the bar were byte-identical with the
    /// accent on and off. Avalonia's own WinUI-Composition backdrop is the only
    /// one that composites here.
    ///
    /// The tint stays the dock colour at the dock's own transparency, so the
    /// existing colour and transparency sliders keep working — the blur only
    /// replaces what shows *through* that tint.
    /// </summary>
    private void ApplyBackdrop()
    {
        if (_appServices == null) return;

        var appearance = _appServices.AppearanceService;
        string mode = appearance.GetBlurMode();

        // Ordered preference: Avalonia walks the list and takes the first level
        // the platform can honour. Note that the Win32 backend does NOT
        // implement WindowTransparencyLevel.Blur — asking for it alone silently
        // degrades to Transparent (measured: ActualTransparencyLevel reported
        // "Transparent", and the background showed through sharp, unblurred).
        // AcrylicBlur is the only level that actually blurs here, so it backs
        // up the plain-blur mode too. Transparent is the tail fallback in every
        // case so the window never drops back to an opaque themed background.
        TransparencyLevelHint = mode switch
        {
            WindowBlur.ModeAcrylic => new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent,
            },
            WindowBlur.ModeBlur => new[]
            {
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent,
            },
            _ => new[] { WindowTransparencyLevel.Transparent },
        };

        // The Border always paints the dock colour at the user's transparency.
        // Unlike the old acrylic accent (which tinted natively and would have
        // doubled up), Avalonia's backdrop applies no colour of its own, so the
        // bar's brush stays in charge in every mode.
        if (DataContext is MainWindowViewModel vm)
            vm.SuppressBarBackground = false;

        if (!WindowBlur.IsEnabled(mode))
        {
            WindowBlur.ClearRegion(this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
            return;
        }

        UpdateBackdropRegion();
    }

    /// <summary>
    /// Re-clips the native window to the dock bar's current rectangle. Called
    /// after every layout change, since SizeToContent means the bar's size
    /// changes whenever items are added or the icon size changes.
    /// </summary>
    private void UpdateBackdropRegion()
    {
        if (_appServices == null) return;
        IntPtr hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        if (!WindowBlur.IsEnabled(_appServices.AppearanceService.GetBlurMode())) return;
        if (DockBar.Bounds.Width <= 0 || DockBar.Bounds.Height <= 0) return;

        // The bar's offset inside the window (the bounce headroom margin).
        var origin = DockBar.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        WindowBlur.SetRoundedRegion(hwnd, origin.X, origin.Y,
            DockBar.Bounds.Width, DockBar.Bounds.Height,
            _appServices.AppearanceService.GetDockBorderRounding(),
            RenderScaling);
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