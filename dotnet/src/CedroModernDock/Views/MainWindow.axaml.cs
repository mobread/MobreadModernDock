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
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Views;

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
            vm.LayerRefreshAction = () => { ApplyAlwaysOnTop(); ApplyAutoHideSetting(); };
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
                var sb = _appServices!.PositioningService.GetPrimaryScreenBounds();
                return ((int)sb.MinX, (int)sb.MinY, (int)sb.MaxX, (int)sb.MaxY);
            },
            blockHide: () => _previewPopup?.IsVisible == true || _folderStack?.IsVisible == true || _reorderInProgress);
        ApplyAutoHideSetting();

        // Static anchors must use the finalized window size, which SizeToContent
        // only produces after the first layout pass. Re-apply once layout settles
        // and whenever the dock content resizes the window.
        if (_appServices?.PositioningService.IsDynamicPositioning() == false)
        {
            SizeChanged += OnDockSizeChanged;
            Dispatcher.UIThread.Post(() => ApplyDockPosition(), DispatcherPriority.Loaded);
        }
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
        if (!force && _appServices.PositioningService.IsDynamicPositioning()) return;
        var (x, y) = _appServices.PositioningService.ResolvePosition(Width, Height);
        if (_autoHide is { IsEnabled: true, IsHidden: true })
        {
            _autoHide.OnLayoutChanged();
            return;
        }
        SetScreenPosition((int)x, (int)y);
    }

    /// <summary>
    /// Where the dock sits when fully shown: the anchored position in STATIC
    /// mode, the persisted position in DYNAMIC mode. Auto-hide slides away
    /// from and back to this point.
    /// </summary>
    private (int X, int Y) RestPosition()
    {
        if (_appServices == null) return GetScreenPosition();
        var (x, y) = _appServices.PositioningService.ResolvePosition(Bounds.Width, Bounds.Height);
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
    }

    private (int X, int Y) GetScreenPosition() =>
        _dockBehavior?.GetScreenPosition() ?? (Position.X, Position.Y);

    private void OnDockSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_appServices?.PositioningService.IsDynamicPositioning() == false)
            ApplyDockPosition();
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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
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
        HideDropIndicator();
        if (!_reorderInProgress) return;
        string? sourceText = e.DataTransfer.TryGetText();
        if (sourceText is null || !int.TryParse(sourceText, out int fromIndex)) return;

        var (gapIndex, _) = ResolvePinnedDropGap(e.GetPosition(PinnedItems));
        (DataContext as MainWindowViewModel)?.MoveItem(fromIndex, gapIndex);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
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
        // Only program items are unpinnable from the dock; the Settings item
        // and Windows modules keep no menu (Settings manages those).
        if (vm.Item is not DockProgramItemModel) return;

        HidePreview();
        var loc = _appServices.LocalizationService;
        var menu = new ContextMenu();
        var unpin = new MenuItem { Header = loc.Text("dock.context.unpin") };
        unpin.Click += (_, _) => mainVm.UnpinItem(vm);
        menu.Items.Add(unpin);
        menu.Open(button);
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
        if (_appServices == null) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        _positionPersistTimer.Stop();
        _positionPersistTimer.Start();
    }

    private void PersistDockPosition()
    {
        if (_appServices == null) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        // Auto-hide moves the window itself; those moves are not user drags.
        if (_autoHide is { IsEnabled: true }) return;
        var (x, y) = GetScreenPosition();
        if (_appServices.AppearanceService.GetEdgeSnapping())
        {
            var rect = ScreenGeometry.WindowScreenRect(this);
            var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            var snapped = EdgeSnapper.Snap(rect, work);
            if (snapped.X != x || snapped.Y != y)
            {
                SetScreenPosition(snapped.X, snapped.Y);
                (x, y) = (snapped.X, snapped.Y);
            }
        }
        _appServices.DockService.SetDockPosition(x, y);
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
        }
        _appServices.PositioningService.SetPositioningMode(mode);
    }

    /// <summary>Current absolute screen position, for callers outside the window (App).</summary>
    public (int X, int Y) CurrentScreenPosition => GetScreenPosition();

    /// <summary>Fullscreen auto-hide: show/hide the native window without changing layering.</summary>
    public void SetNativeVisible(bool visible) => _dockBehavior?.SetNativeVisible(visible);

    /// <summary>Re-reads the always-on-top setting and switches the window layer live.</summary>
    public void ApplyAlwaysOnTop()
    {
        if (_appServices == null || _dockBehavior == null) return;
        _dockBehavior.SetAlwaysOnTop(_appServices.AppearanceService.GetAlwaysOnTop());
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
        // taskbar would stay hidden.
        App.RequestShutdown();
    }
}