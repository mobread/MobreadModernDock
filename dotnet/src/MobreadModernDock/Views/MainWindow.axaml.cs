using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private DockAutoHideController? _autoHide;
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
            vm.LayerRefreshAction = () => { ApplyAlwaysOnTop(); ApplyAutoHideSetting(); ApplyBackdrop(); SyncPinnedPanel(); };
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
        var panel = PinnedItems.GetVisualDescendants().OfType<DockItemsPanel>().FirstOrDefault();
        if (panel == null) return;
        panel.Lines = vm.DockLines;
        panel.IsVertical = vm.IsVerticalDock;
        // Magnification is single-line only; the service already folds that in.
        panel.MagnifyScale = _appServices?.AppearanceService.GetMagnifyIcons() == true
            ? _appServices.AppearanceService.GetMagnifyScale()
            : 1.0;
        // The per-item hover zoom must stand down while the panel is scaling.
        PinnedItems.Classes.Set("magnified", panel.MagnifyScale > 1.0);
        if (panel.MagnifyScale <= 1.0) panel.UpdateMagnification(null);
    }

    /// <summary>
    /// Feeds the pointer position to the items panel so it can magnify. The
    /// position is taken along the dock's main axis in panel coordinates,
    /// which is exactly what DockMagnification expects.
    /// </summary>
    private void UpdateMagnifier(PointerEventArgs e)
    {
        var panel = PinnedItems.GetVisualDescendants().OfType<DockItemsPanel>().FirstOrDefault();
        if (panel is null) return;

        // Re-read the setting here rather than trusting a value pushed in
        // earlier: SyncPinnedPanel can run before the panel is realized, and
        // a stale 1.0 would silently disable the effect for the whole session.
        double scale = _appServices?.AppearanceService.GetMagnifyIcons() == true
            ? _appServices.AppearanceService.GetMagnifyScale()
            : 1.0;
        if (panel.MagnifyScale != scale)
        {
            panel.MagnifyScale = scale;
            PinnedItems.Classes.Set("magnified", scale > 1.0);
        }
        if (scale <= 1.0)
        {
            panel.UpdateMagnification(null);
            return;
        }

        var p = e.GetPosition(panel);
        panel.UpdateMagnification(panel.IsVertical ? p.Y : p.X);
    }

    private void ClearMagnifier()
    {
        var panel = PinnedItems.GetVisualDescendants().OfType<DockItemsPanel>().FirstOrDefault();
        panel?.UpdateMagnification(null);
    }

    private void OnItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not DockItemViewModel vm || vm.Item is not DockProgramItemModel item)
            return;

        _hoveredButton = button;
        _previewHideDebounce.Stop();
        int requestId = ++_previewRequestId;
        var programItem = item;
        string label = vm.Label;
        _previewExecutablePath = programItem.ExecutablePath;
        _previewLabel = label;

        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                windows = _appServices.WindowPreviewService.LoadPreview(programItem);
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

        _hoveredButton = button;
        _previewHideDebounce.Stop();
        int requestId = ++_previewRequestId;
        string executablePath = vm.ExecutablePath;
        string label = vm.Label;
        _previewExecutablePath = executablePath;
        _previewLabel = label;

        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                // Build a throwaway program item to reuse the preview loader.
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
            return;
        }
        _previewPopup.ShowFor(windows, label,
            appearance.GetDockColorRGB(), appearance.GetDockBorderRounding(),
            appearance.GetDockTransparencyPercentage() / 100.0, button,
            verticalDock: _appServices.AppearanceService.GetVerticalDock(),
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor());
    }

    private void OnItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredButton == sender) _hoveredButton = null;
        _previewHideDebounce.Stop();
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

        if (target?.DataContext is DockItemViewModel itemVm && vm.OpenFilesWith(itemVm, paths))
        {
            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        var (gapIndex, _) = ResolvePinnedDropGap(e.GetPosition(PinnedItems));
        vm.PinDroppedPaths(paths, gapIndex);
        e.DragEffects = DragDropEffects.Link;
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

            menu.Open(button);
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

        menu.Open(button);
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
        menu.Open(button);
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
    /// Applies the configured backdrop (none / blur / acrylic) to the native
    /// window and clips it to the dock bar so the blur follows the rounded
    /// corners instead of filling the whole window rectangle (which includes
    /// the transparent bounce headroom).
    ///
    /// The tint is the dock colour at the dock's own transparency, so the
    /// existing colour and transparency sliders keep working — the blur only
    /// replaces what shows *through* that tint.
    /// </summary>
    private void ApplyBackdrop()
    {
        if (_appServices == null) return;
        IntPtr hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        var appearance = _appServices.AppearanceService;
        string mode = appearance.GetBlurMode();

        if (!WindowBlur.IsEnabled(mode))
        {
            WindowBlur.Apply(hwnd, WindowBlur.ModeNone, 0, 0, 0, 0);
            WindowBlur.ClearRegion(hwnd);
            // The Border paints the background again once the backdrop is off.
            if (DataContext is MainWindowViewModel novm) novm.SuppressBarBackground = false;
            return;
        }

        var (r, g, b) = ParseRgb(appearance.GetDockColorRGB());
        double tint = appearance.GetDockTransparencyPercentage() / 100.0;
        WindowBlur.Apply(hwnd, mode, r, g, b, tint);

        // Plain blur draws no tint of its own, so the Border keeps painting the
        // dock colour over it. Acrylic already applies the tint natively —
        // letting the Border paint it again would double the opacity.
        if (DataContext is MainWindowViewModel vm)
            vm.SuppressBarBackground = mode == WindowBlur.ModeAcrylic;

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

    private static (byte R, byte G, byte B) ParseRgb(string rgb)
    {
        var parts = rgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        return (r, g, b);
    }

    protected override void OnClosed(EventArgs e)
    {
        PositionChanged -= OnDockPositionChanged;
        _positionPersistTimer.Stop();
        HidePreview();
        if (DataContext is MainWindowViewModel vm)
            vm.Shutdown();
        _autoHide?.Dispose();
        _autoHide = null;
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