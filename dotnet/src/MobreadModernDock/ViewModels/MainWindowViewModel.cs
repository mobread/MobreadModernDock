using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Native;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// Main dock ViewModel. Holds the dock items collection, appearance settings,
/// and the running-app indicator watcher, expressed as MVVM bindings.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices? _appServices;
    private CancellationTokenSource? _indicatorCts;

    private int _iconsSize = 48;
    private int _spacing = 5;
    private int _borderRounding = 12;
    private IBrush _dockBackground = new SolidColorBrush(Color.FromArgb(158, 0, 0, 0));
    private string _statusText = "";

    public ObservableCollection<DockItemViewModel> Items { get; } = new();

    /// <summary>Running-but-unpinned programs shown right of the separator (taskbar-like).</summary>
    public ObservableCollection<RunningAppViewModel> RunningApps { get; } = new();

    private bool _hasRunningApps;
    public bool HasRunningApps
    {
        get => _hasRunningApps;
        private set
        {
            if (SetProperty(ref _hasRunningApps, value))
            {
                OnPropertyChanged(nameof(ShowHorizontalSeparator));
                OnPropertyChanged(nameof(ShowVerticalSeparator));
            }
        }
    }

    private bool _isVerticalDock;
    public bool IsVerticalDock
    {
        get => _isVerticalDock;
        set
        {
            if (SetProperty(ref _isVerticalDock, value))
            {
                OnPropertyChanged(nameof(DockOrientation));
                OnPropertyChanged(nameof(ShowHorizontalSeparator));
                OnPropertyChanged(nameof(ShowVerticalSeparator));
                // The magnification headroom swaps axes with the dock.
                OnPropertyChanged(nameof(DockBarMargin));
            }
        }
    }

    /// <summary>Layout direction of the dock items (horizontal by default, vertical when enabled).</summary>
    public Avalonia.Layout.Orientation DockOrientation
        => IsVerticalDock ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;

    /// <summary>
    /// Where item tooltips open, so they never cover the icon: on the side
    /// of the dock that faces the screen centre. Recomputed from the dock's
    /// screen position by the window (see <see cref="UpdateTooltipPlacement"/>).
    /// </summary>
    private PlacementMode _tooltipPlacement = PlacementMode.Top;
    public PlacementMode TooltipPlacement { get => _tooltipPlacement; private set => SetProperty(ref _tooltipPlacement, value); }

    /// <summary>Pick the tooltip side from the dock's rect vs. its screen's work area.</summary>
    public void UpdateTooltipPlacement(int dockX, int dockY, int dockW, int dockH, int workL, int workT, int workR, int workB)
    {
        if (IsVerticalDock)
        {
            int dLeft = dockX - workL, dRight = workR - (dockX + dockW);
            TooltipPlacement = dLeft <= dRight ? PlacementMode.Right : PlacementMode.Left;
        }
        else
        {
            int dTop = dockY - workT, dBottom = workB - (dockY + dockH);
            TooltipPlacement = dTop <= dBottom ? PlacementMode.Bottom : PlacementMode.Top;
        }
    }

    /// <summary>Separator is only shown between pinned items and displayed unpinned running apps.</summary>
    public bool ShowHorizontalSeparator => HasRunningApps && !IsVerticalDock;

    /// <summary>Separator is only shown between pinned items and displayed unpinned running apps.</summary>
    public bool ShowVerticalSeparator => HasRunningApps && IsVerticalDock;

    // --- Multi-row layout of the pinned items ---
    // The pinned ItemsControl uses a DockItemsPanel. For a horizontal dock the
    // configured value is the number of rows; for a vertical dock it is the
    // number of columns. The window pushes this into the panel (an
    // ItemsPanelTemplate has no DataContext), so no grid shape is computed
    // here any more.
    private int _dockLines = 1;
    public int DockLines
    {
        get => _dockLines;
        set => SetProperty(ref _dockLines, Math.Max(1, value));
    }

    /// <summary>
    /// Half the configured spacing on every side of each pinned item, so
    /// neighbours are exactly <see cref="Spacing"/> apart in both directions.
    /// </summary>
    public Thickness CellMargin => new(Spacing / 2.0);

    public int IconsSize
    {
        get => _iconsSize;
        set => SetProperty(ref _iconsSize, value);
    }
    public int Spacing
    {
        get => _spacing;
        set
        {
            if (SetProperty(ref _spacing, value))
                OnPropertyChanged(nameof(CellMargin));
        }
    }
    public int BorderRounding
    {
        get => _borderRounding;
        set
        {
            if (SetProperty(ref _borderRounding, value))
                OnPropertyChanged(nameof(DockCornerRadius));
        }
    }
    public CornerRadius DockCornerRadius => new CornerRadius(BorderRounding);

    private int _dockPadding = 10;
    /// <summary>
    /// Padding inside the dock bar. Exposed as a Thickness for the Border;
    /// with the icon size it is what makes a "compact" dock actually short.
    /// </summary>
    public int DockPaddingValue
    {
        get => _dockPadding;
        set { if (SetProperty(ref _dockPadding, value)) OnPropertyChanged(nameof(DockPadding)); }
    }
    public Thickness DockPadding => new Thickness(_dockPadding);

    private double _magnifyOverhang;
    /// <summary>
    /// Transparent headroom, in layout units, reserved either side of the bar
    /// so magnified end icons are not sliced off at the window's edge.
    ///
    /// A window cannot paint outside its own bounds, so <c>ClipToBounds</c>
    /// alone cannot save them: the room has to exist in the window. The dock
    /// already reserves 12px above the bar for the attention bounce; this is
    /// the same idea along the dock's main axis, sized from the magnification
    /// settings (see <see cref="DockMagnification.MaxOverhang"/>) and 0 while
    /// magnification is off, so a non-magnifying dock keeps its exact old size.
    /// </summary>
    public double MagnifyOverhang
    {
        get => _magnifyOverhang;
        set
        {
            if (SetProperty(ref _magnifyOverhang, value))
                OnPropertyChanged(nameof(DockBarMargin));
        }
    }

    /// <summary>
    /// Headroom on the far side of the bar (above a horizontal dock, beside
    /// a vertical one), in layout units. At least the 12px the attention
    /// bounce needs; more while magnification is on, because a magnified
    /// icon scales from the edge it rests on and grows <i>away</i> from the
    /// screen edge by <c>iconSize × (scale − 1)</c> - past the bar and, with
    /// only the bounce headroom, past the window, where it cannot paint and
    /// is sliced flat. Set alongside <see cref="MagnifyOverhang"/>.
    /// </summary>
    public double CrossHeadroom
    {
        get => _crossHeadroom;
        set
        {
            if (SetProperty(ref _crossHeadroom, value))
                OnPropertyChanged(nameof(DockBarMargin));
        }
    }
    private double _crossHeadroom = BounceHeadroom;

    public const double BounceHeadroom = 12;

    /// <summary>
    /// Margin around the dock bar: cross-axis headroom on the far side (bounce
    /// and magnification growth), plus magnification headroom along the main
    /// axis. A vertical dock rests on the right edge of its slot (see the
    /// panel's RenderTransformOrigin), so its magnification headroom is on the
    /// left; the bounce is always vertical, so it keeps its room on top.
    /// </summary>
    public Thickness DockBarMargin => IsVerticalDock
        ? new Thickness(_crossHeadroom - BounceHeadroom, BounceHeadroom + _magnifyOverhang, 0, _magnifyOverhang)
        : new Thickness(_magnifyOverhang, _crossHeadroom, _magnifyOverhang, 0);

    private int _previewDelayMs = 400;
    /// <summary>
    /// Hover delay before an item's tooltip and its window preview appear.
    /// Bound by the item template's ToolTip.ShowDelay; the window applies the
    /// same value to the preview timer. When a preview does appear it takes
    /// the tooltip's place (MainWindow.SuppressTooltip), so the shared delay
    /// costs nothing.
    /// </summary>
    public int PreviewDelayMs { get => _previewDelayMs; set => SetProperty(ref _previewDelayMs, value); }
    public IBrush DockBackground
    {
        get => _dockBackground;
        set => SetProperty(ref _dockBackground, value);
    }

    private double _windowOpacity = 1.0;
    /// <summary>Whole-dock opacity (icons + background), from the global opacity setting.</summary>
    public double WindowOpacity { get => _windowOpacity; set => SetProperty(ref _windowOpacity, value); }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ICommand LaunchCommand { get; }

    /// <summary>Set by MainWindow — opens the settings window with this window as owner.</summary>
    public Action? OpenSettingsAction { get; set; }

    /// <summary>Set by MainWindow — re-anchors the dock after dock content/settings change.</summary>
    public Action? RepositionAction { get; set; }

    /// <summary>Set by MainWindow — re-applies the always-on-top / desktop layer.</summary>
    public Action? LayerRefreshAction { get; set; }

    /// <summary>Set by MainWindow — shows a folder stack popup for a folder item. Return false to fall back to Explorer.</summary>
    public Func<DockItemViewModel, bool>? ShowFolderStackAction { get; set; }

    /// <summary>Set by MainWindow — dismisses the window-preview popup (dock refresh).</summary>
    public Action? PreviewDismissAction { get; set; }

    public MainWindowViewModel()
    {
        LaunchCommand = new RelayCommand(_ => { });
    }

    public MainWindowViewModel(AppServices appServices)
    {
        _appServices = appServices;
        LaunchCommand = new RelayCommand(param => ExecuteItem(param));
        _appServices.LocalizationService.AddListener(UpdateDockUI);
    }

    public void Initialize()
    {
        if (_appServices == null) return;
        ApplyAppearance();
        UpdateDockUI();
        StartIndicatorWatcher();
        StartAttentionMonitor();
    }

    // --- #13 attention bounce ---

    private Infrastructure.Windows.Native.AttentionMonitor? _attention;

    private void StartAttentionMonitor()
    {
        if (IsMirrorViewModel) return; // the primary dock's monitor sets NeedsAttention; mirrors would fight over the static instance
        try { _attention = new Infrastructure.Windows.Native.AttentionMonitor(OnAppFlashed); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Attention] {e.Message}"); }
    }

    /// <summary>Windows currently asking for attention, by executable. Cleared when one is focused or its icon clicked.</summary>
    private readonly Dictionary<string, HashSet<IntPtr>> _flashing = new(StringComparer.OrdinalIgnoreCase);

    private void OnAppFlashed(string executablePath, IntPtr hwnd)
    {
        if (_appServices == null || !_appServices.AppearanceService.GetAttentionBounce()) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_flashing.TryGetValue(executablePath, out var set)) _flashing[executablePath] = set = new();
            set.Add(hwnd);
            SetAttention(executablePath, true);
            App.ForEachMirrorViewModel(vm => vm.SetAttention(executablePath, true));
        });
    }

    /// <summary>Flag/unflag every VM (pinned or running) that maps to this executable.</summary>
    public void SetAttention(string executablePath, bool on)
    {
        foreach (var item in Items)
            if (item.ExecutablePath != null && string.Equals(item.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                item.NeedsAttention = on;
        if (_runningAppsByPath.TryGetValue(executablePath, out var running))
            running.NeedsAttention = on;
    }

    /// <summary>Stop bouncing: called when the user clicks the icon or the flashing window comes to the foreground.</summary>
    public void ClearAttention(string? executablePath)
    {
        if (executablePath == null) return;
        if (IsMirrorViewModel) { App.PrimaryViewModel?.ClearAttention(executablePath); return; }
        _flashing.Remove(executablePath);
        SetAttention(executablePath, false);
        App.ForEachMirrorViewModel(vm => vm.SetAttention(executablePath, false));
    }

    /// <summary>
    /// A flashing window that reaches the foreground no longer needs attention.
    /// Polled alongside the running indicators. Checks the specific HWNDs that
    /// flashed — the app may have other windows already in front.
    /// </summary>
    private void ClearAttentionForForeground()
    {
        if (_appServices == null || IsMirrorViewModel || _flashing.Count == 0) return;
        string? done = null;
        foreach (var (exe, hwnds) in _flashing)
            if (hwnds.Any(Infrastructure.Windows.Native.AttentionMonitor.IsForeground)) { done = exe; break; }
        if (done != null) Avalonia.Threading.Dispatcher.UIThread.Post(() => ClearAttention(done));
    }

    /// <summary>
    /// Drag-reorder on the dock bar: moves the pinned item shown at
    /// <paramref name="fromIndex"/> (index into <see cref="Items"/>) into the
    /// gap at <paramref name="toIndex"/> (0..Items.Count). Indices are mapped
    /// to the underlying model list so a view-model that was skipped during
    /// creation can never shift the target. Persists and rebuilds the dock.
    /// </summary>
    public void MoveItem(int fromIndex, int toIndex)
    {
        if (_appServices == null) return;
        if (fromIndex < 0 || fromIndex >= Items.Count) return;
        toIndex = Math.Clamp(toIndex, 0, Items.Count);
        if (fromIndex == toIndex || fromIndex + 1 == toIndex) return;

        var modelItems = _appServices.DockService.GetItems();
        int modelFrom = modelItems.IndexOf(Items[fromIndex].Item);
        int modelTo = toIndex < Items.Count
            ? modelItems.IndexOf(Items[toIndex].Item)
            : modelItems.Count;
        if (modelFrom < 0 || modelTo < 0) return;

        _appServices.DockService.MoveItem(modelFrom, modelTo);
        RefreshAllDocks();
    }

    /// <summary>
    /// Pins a running-but-unpinned app: adds it as a program item at the end
    /// of the pinned list, persists, and rebuilds the dock. The running-apps
    /// watcher drops it from the right-hand section on its next pass.
    /// </summary>
    public void PinRunningApp(RunningAppViewModel app)
    {
        if (_appServices == null || string.IsNullOrWhiteSpace(app.ExecutablePath)) return;
        var sel = ProgramSelectionResolver.Resolve(app.ExecutablePath);
        bool alreadyPinned = _appServices.DockService.GetItems()
            .OfType<DockProgramItemModel>()
            .Any(p => string.Equals(p.ExecutablePath, sel.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (alreadyPinned) return;

        _appServices.DockService.AddItem(new DockProgramItemModel(sel.Label, sel.ExecutablePath));
        RefreshAllDocks();
        Task.Run(RefreshRunningApps);
    }

    /// <summary>
    /// Unpins a program item from the dock. Only program items can be
    /// unpinned this way; the Settings item and Windows modules are managed
    /// from the Settings window.
    /// </summary>
    public void UnpinItem(DockItemViewModel item)
    {
        if (_appServices == null || item.Item is not DockProgramItemModel) return;
        int index = _appServices.DockService.GetItems().IndexOf(item.Item);
        if (index < 0) return;
        _appServices.DockService.RemoveItem(index);
        RefreshAllDocks();
        Task.Run(RefreshRunningApps);
    }

    /// <summary>
    /// Sets or clears a dock item's custom icon. Passing null restores the
    /// icon resolved from the target (exe/folder/module).
    /// </summary>
    public void SetCustomIcon(DockItemViewModel item, string? iconPath)
    {
        if (_appServices == null) return;
        int index = _appServices.DockService.GetItems().IndexOf(item.Item);
        if (index < 0) return;
        _appServices.DockService.SetCustomIcon(index, iconPath);
        RefreshAllDocks();
    }

    /// <summary>
    /// Inserts a divider directly after the given item (right-click → Add
    /// separator). Passing null appends it before the Settings gear.
    /// </summary>
    public void AddSeparatorAfter(DockItemViewModel? item)
    {
        if (_appServices == null) return;
        var items = _appServices.DockService.GetItems();
        int index = item != null ? items.IndexOf(item.Item) + 1 : items.Count;
        _appServices.DockService.InsertItem(new DockSeparatorItemModel(), index);
        RefreshAllDocks();
    }

    /// <summary>Removes a divider from the dock (right-click → Remove separator).</summary>
    public void RemoveSeparator(DockItemViewModel item)
    {
        if (_appServices == null || item.Item is not DockSeparatorItemModel) return;
        int index = _appServices.DockService.GetItems().IndexOf(item.Item);
        if (index < 0) return;
        _appServices.DockService.RemoveItem(index);
        RefreshAllDocks();
    }

    /// <summary>
    /// Sets a divider's blank width (fraction of the icon size) and whether
    /// its hairline is drawn (right-click → Spacing / Hide line).
    /// </summary>
    public void SetSeparatorSpacing(DockItemViewModel item, double spacing, bool hideLine)
    {
        if (_appServices == null || item.Item is not DockSeparatorItemModel) return;
        int index = _appServices.DockService.GetItems().IndexOf(item.Item);
        if (index < 0) return;
        _appServices.DockService.SetSeparatorSpacing(index, spacing, hideLine);
        RefreshAllDocks();
    }

    /// <summary>
    /// Pins files or folders dropped from Explorer onto the dock at the given
    /// gap index. Executables and shortcuts become program items, directories
    /// become folder items, and any other file is pinned as a program item
    /// opened by its default handler. Already-pinned targets are skipped.
    /// Returns how many items were added.
    /// </summary>
    public int PinDroppedPaths(IReadOnlyList<string> paths, int gapIndex)
    {
        if (_appServices == null || paths.Count == 0) return 0;

        var existing = new HashSet<string>(
            _appServices.DockService.GetItems().OfType<DockProgramItemModel>().Select(p => p.ExecutablePath),
            StringComparer.OrdinalIgnoreCase);
        var existingFolders = new HashSet<string>(
            _appServices.DockService.GetItems().OfType<DockFolderItemModel>().Select(f => f.FolderPath),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var path in paths)
        {
            DockItem? item = BuildDroppedItem(path);
            if (item == null) continue;

            if (item is DockProgramItemModel p)
            {
                if (!existing.Add(p.ExecutablePath)) continue;
                _appServices.IconGateway.CacheProgramIcon(p.ExecutablePath);
            }
            else if (item is DockFolderItemModel f)
            {
                if (!existingFolders.Add(f.FolderPath)) continue;
                _appServices.IconGateway.CacheFolderIcon(f.FolderPath);
            }

            // Each insert shifts the following ones, so drops keep their order.
            _appServices.DockService.InsertItem(item, gapIndex + added);
            added++;
        }

        if (added > 0)
        {
            RefreshAllDocks();
            Task.Run(RefreshRunningApps);
        }
        return added;
    }

    /// <summary>
    /// Turns a dropped path into a dock item: directory → folder item,
    /// .lnk → its resolved target, .exe → program item. Any other file is
    /// pinned as a program item too — launching it goes through the shell,
    /// which opens it with its registered handler.
    /// </summary>
    private static DockItem? BuildDroppedItem(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                return new DockFolderItemModel(string.IsNullOrWhiteSpace(name) ? path : name, path);
            }
            if (!File.Exists(path)) return null;

            if (ShellLinkResolver.IsShortcut(path))
            {
                var link = ShellLinkResolver.Resolve(path);
                if (link == null || string.IsNullOrWhiteSpace(link.TargetPath)) return null;
                // A shortcut to a folder pins the folder itself.
                if (Directory.Exists(link.TargetPath))
                    return new DockFolderItemModel(
                        Path.GetFileNameWithoutExtension(path), link.TargetPath);
                if (!File.Exists(link.TargetPath)) return null;
                var target = ProgramSelectionResolver.Resolve(link.TargetPath);
                return new DockProgramItemModel(
                    Path.GetFileNameWithoutExtension(path), target.ExecutablePath, link.Arguments);
            }

            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ProgramSelectionResolver.Resolve(path);
                return new DockProgramItemModel(resolved.Label, resolved.ExecutablePath);
            }

            return new DockProgramItemModel(Path.GetFileNameWithoutExtension(path), path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Files dropped onto a program icon are opened with that program.
    /// Returns false when the item is not a program (folders, modules and the
    /// Settings gear don't take drops).
    /// </summary>
    public bool OpenFilesWith(DockItemViewModel item, IReadOnlyList<string> filePaths)
    {
        if (_appServices == null || filePaths.Count == 0) return false;
        if (item.Item is not DockProgramItemModel program) return false;
        PreviewDismissAction?.Invoke();
        return _appServices.ItemActionService.OpenWith(program, filePaths);
    }

    /// <summary>
    /// Rebuilds <b>every</b> dock after a shared setting or item change. A
    /// mirror's <see cref="UpdateDockUI"/> refreshes only itself (it must not
    /// recurse into SyncMirrorDocks, which calls it), so any change made from
    /// a mirror — its context menu, a drop, or Settings opened from its gear —
    /// has to be routed through the primary, whose refresh cascades.
    /// </summary>
    public void RefreshAllDocks()
    {
        if (IsMirrorViewModel && App.PrimaryViewModel is { } primary && primary != this)
            primary.UpdateDockUI();
        else
            UpdateDockUI();
    }

    public void UpdateDockUI()
    {
        if (_appServices == null) return;
        IsVerticalDock = _appServices.AppearanceService.GetVerticalDock();
        // The tint must be set BEFORE items are created: CreateItemViewModel
        // tints each icon immediately, and a stale value would make every
        // pinned item lag one color selection behind.
        var appearance = _appServices.AppearanceService;
        IconTinter.ActiveColor = appearance.GetTintIcons()
            ? ParseRgbColor(appearance.GetTintColorRGB())
            : null;
        Items.Clear();
        var dock = _appServices.DockService.GetDock();
        var loc = _appServices.LocalizationService;

        foreach (var item in dock.Items)
        {
            var vm = CreateItemViewModel(item, loc);
            if (vm != null)
            {
                vm.IconSize = _appServices.AppearanceService.GetIconsSize();
                Items.Add(vm);
            }
        }
        ApplyAppearance();
        RepositionAction?.Invoke();
        PreviewDismissAction?.Invoke();
        // LayerRefreshAction also re-syncs the items panel's rows/orientation.
        LayerRefreshAction?.Invoke();
        App.RefreshWidgetAppearance();
        if (!IsMirrorViewModel)
        {
            App.SyncMirrorDocks();
            // Launch chords index into the pinned programs, so any reorder /
            // pin / unpin re-maps them.
            App.ApplyHotkeys();
        }
    }

    /// <summary>#10 Set on VMs that back a secondary-monitor mirror so they don't recurse into SyncMirrorDocks.</summary>
    public bool IsMirrorViewModel { get; init; }
    // --- continued below ---

    private DockItemViewModel? CreateItemViewModel(DockItem item, LocalizationService loc)
    {
        string label = loc.DockItemLabel(item);

        // A user-chosen icon wins over everything else. A missing/invalid file
        // falls through to the normal resolution below.
        var custom = IconLoader.LoadCustomIcon(item.CustomIcon);

        // A user-placed divider: no icon, no action, no tooltip.
        if (item is DockSeparatorItemModel)
        {
            return new DockItemViewModel(item, "", LaunchCommand)
            {
                IsVerticalDock = IsVerticalDock
            };
        }

        if (item is DockSettingsItemModel)
        {
            var icon = custom ?? IconLoader.LoadFromAsset(IconLoader.MapResourcePath(item.Path));
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockWindowsModuleItemModel moduleItem)
        {
            var icon = custom ?? IconLoader.LoadWindowsModuleIcon(moduleItem.Module);
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockProgramItemModel programItem)
        {
            string? iconPath = _appServices!.IconGateway.ResolveProgramIcon(programItem.ExecutablePath);
            var icon = custom ?? IconLoader.LoadFromFile(iconPath);
            var itemVm = new DockItemViewModel(item, label, LaunchCommand,
                showIndicator: true, executablePath: programItem.ExecutablePath)
            {
                Icon = IconTinter.Apply(icon)
            };

            // The icon may not be cached yet (first run with a new item). Extract it in the
            // background, then push the resulting bitmap into the ViewModel so the dock
            // updates without requiring a restart.
            if (icon == null)
            {
                string exe = programItem.ExecutablePath;
                _ = Task.Run(() =>
                {
                    _appServices.IconGateway.CacheProgramIcon(exe);
                    string? cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    var loaded = IconLoader.LoadFromFile(cached);
                    if (loaded != null)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => itemVm.Icon = IconTinter.Apply(loaded));
                });
            }

            return itemVm;
        }

        if (item is DockFolderItemModel folderItem)
        {
            string? iconPath = _appServices!.IconGateway.ResolveFolderIcon(folderItem.FolderPath);
            var icon = custom ?? IconLoader.LoadFromFile(iconPath);
            if (icon == null)
            {
                _ = Task.Run(() => _appServices.IconGateway.CacheFolderIcon(folderItem.FolderPath));
                icon = IconLoader.LoadFromAsset("Assets/icons/folder.png");
            }
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        return null;
    }

    private void ApplyAppearance()
    {
        if (_appServices == null) return;
        var appearance = _appServices.AppearanceService;

        IconsSize = appearance.GetIconsSize();
        PreviewDelayMs = appearance.GetPreviewDelayMs();
        foreach (var app in RunningApps)
            app.IconSize = IconsSize;
        Spacing = appearance.GetSpacingBetweenIcons();
        DockLines = appearance.GetDockRows();
        WindowOpacity = appearance.GetGlobalOpacityPercentage() / 100.0;
        BorderRounding = appearance.GetDockBorderRounding();
        DockPaddingValue = appearance.GetDockPadding();

        // Re-tint the persistent running-apps VMs with the current tint
        // (set in UpdateDockUI before items are created; pinned items are
        // recreated on every refresh so they are tinted at creation).
        foreach (var app in RunningApps)
            app.Icon = IconTinter.Apply(app.OriginalIcon);

        string colorRgb = appearance.GetDockColorRGB();
        double transparency = appearance.GetDockTransparencyPercentage() / 100.0;
        byte alpha = (byte)(transparency * 255);
        var parts = colorRgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        DockBackground = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    private static Color ParseRgbColor(string rgb)
    {
        var parts = rgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        return Color.FromRgb(r, g, b);
    }

    private void ExecuteItem(object? param)
    {
        if (param is DockItemViewModel itemVm && _appServices != null)
        {
            // A divider is not clickable.
            if (itemVm.Item is DockSeparatorItemModel) return;

            ClearAttention(itemVm.ExecutablePath);
            if (itemVm.Item is DockFolderItemModel
                && _appServices.AppearanceService.GetFolderStacks()
                && ShowFolderStackAction?.Invoke(itemVm) == true)
                return;

            // Power actions that end the session ask first — a mis-click on a
            // dock icon should never close every open document.
            if (itemVm.Item is DockWindowsModuleItemModel module
                && DockWindowsModuleItemModel.NeedsConfirmation(module.Module)
                && !ConfirmPowerAction(module))
                return;

            // Taskbar semantics for running programs: focus / minimize /
            // cycle instead of launching another instance.
            if (itemVm.Item is DockProgramItemModel running
                && _appServices.WindowPreviewService.ClickRunning(running))
            {
                PreviewDismissAction?.Invoke();
                return;
            }

            bool launched = _appServices.ItemActionService.Execute(
                itemVm.Item, () => OpenSettingsAction?.Invoke());

            // One macOS-style hop as "got it" feedback for a fresh launch.
            // Focusing an already-running app (handled above) doesn't hop.
            if (launched && itemVm.ShowIndicator && !itemVm.IsRunning
                && _appServices.AppearanceService.GetLaunchBounce())
                itemVm.BounceOnce();

            if (!launched && itemVm.Item is DockProgramItemModel programItem)
            {
                var loc = _appServices.LocalizationService;
                System.Windows.Forms.MessageBox.Show(
                    loc.Text("dialog.programNotFound.message",
                        programItem.Label, programItem.ExecutablePath),
                    loc.Text("dialog.programNotFound.title"),
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>
    /// Confirmation prompt for the session-ending power actions. Uses the
    /// same WinForms MessageBox as the "program not found" dialog — the dock
    /// window is WS_EX_NOACTIVATE, so an Avalonia dialog owned by it cannot
    /// take focus and would sit there unfocused behind the pointer.
    /// </summary>
    private bool ConfirmPowerAction(DockWindowsModuleItemModel module)
    {
        if (_appServices == null) return false;
        var loc = _appServices.LocalizationService;
        string label = loc.DockItemLabel(module);
        var result = System.Windows.Forms.MessageBox.Show(
            loc.Text("dialog.powerAction.message", label),
            loc.Text("dialog.powerAction.title"),
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Warning,
            System.Windows.Forms.MessageBoxDefaultButton.Button2);
        return result == System.Windows.Forms.DialogResult.Yes;
    }

    private void StartIndicatorWatcher()
    {
        _indicatorCts?.Cancel();
        _indicatorCts = new CancellationTokenSource();
        var token = _indicatorCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    RefreshIndicators();
                    RefreshRunningApps();
                    ClearAttentionForForeground();
                    await Task.Delay(1200, token);
                }
                catch (OperationCanceledException) { break; }
                catch { /* keep watching */ }
            }
        }, token);
    }

    private void RefreshIndicators()
    {
        if (_appServices == null) return;
        var programItems = Items.Where(i => i.ShowIndicator && i.ExecutablePath != null).ToList();
        foreach (var item in programItems)
        {
            bool isOpen = _appServices.WindowPreviewService.HasOpenWindows(item.ExecutablePath);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => item.IsRunning = isOpen);
        }
    }

    private readonly Dictionary<string, RunningAppViewModel> _runningAppsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Syncs the running-apps section: taskbar-visible windows, grouped by executable,
    /// minus programs already pinned in the dock. Only touches the UI when the set
    /// actually changed, so the preview popup never flickers.
    /// </summary>
    private void RefreshRunningApps()
    {
        if (_appServices == null) return;

        // Feature toggle: only show unpinned running apps when enabled.
        if (!_appServices.AppearanceService.GetShowUnpinnedRunningApps())
        {
            if (RunningApps.Count > 0)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RunningApps.Clear();
                    _runningAppsByPath.Clear();
                    HasRunningApps = false;
                    RepositionAction?.Invoke();
                    PreviewDismissAction?.Invoke();
                });
            }
            return;
        }

        List<string> pinnedPaths = Items
            .Where(i => i.Item is DockProgramItemModel)
            .Select(i => ((DockProgramItemModel)i.Item).ExecutablePath)
            .ToList();

        List<MobreadModernDock.Core.Domain.RunningWindowInfo> windows;
        try
        {
            windows = _appServices.WindowPreviewService.FindTaskbarWindows();
        }
        catch
        {
            return;
        }

        var desired = windows
            .GroupBy(w => w.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => !IsPinned(g.Key, pinnedPaths))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_appServices == null) return;

            bool changed = false;

            // Remove apps that closed or got pinned.
            foreach (var path in _runningAppsByPath.Keys.ToList())
            {
                if (!desired.ContainsKey(path) || IsPinned(path, pinnedPaths))
                {
                    RunningApps.Remove(_runningAppsByPath[path]);
                    _runningAppsByPath.Remove(path);
                    changed = true;
                }
            }

            // Add newly opened apps.
            foreach (var (path, win) in desired)
            {
                if (_runningAppsByPath.ContainsKey(path)) continue;
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var vm = new RunningAppViewModel(path, label)
                {
                    IconSize = _appServices.AppearanceService.GetIconsSize()
                };
                LoadRunningIcon(vm, win.Handle);
                _runningAppsByPath[path] = vm;
                RunningApps.Add(vm);
                changed = true;
            }

            if (changed)
            {
                HasRunningApps = RunningApps.Count > 0;
                RepositionAction?.Invoke();
                PreviewDismissAction?.Invoke();
            }
        });
    }

    private static bool IsPinned(string executablePath, List<string> pinnedPaths)
    {
        string file = System.IO.Path.GetFileName(executablePath);
        foreach (var pinned in pinnedPaths)
        {
            if (string.Equals(pinned, executablePath, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(System.IO.Path.GetFileName(pinned), file, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void LoadRunningIcon(RunningAppViewModel vm, IntPtr windowHandle)
    {
        if (_appServices == null) return;
        string exe = vm.ExecutablePath;

        // The actual Windows Settings app (SystemSettings.exe) carries no icon
        // in its EXE and its UWP frame exposes none either, so its dock icon is
        // hardcoded to the bundled Windows Settings asset.
        if (string.Equals(System.IO.Path.GetFileName(exe),
                "SystemSettings.exe", StringComparison.OrdinalIgnoreCase))
        {
            var settingsIcon = IconLoader.LoadFromAsset("Assets/icons/windows_settings.png");
            if (settingsIcon != null)
            {
                vm.OriginalIcon = settingsIcon;
                vm.Icon = IconTinter.Apply(settingsIcon);
                return;
            }
        }

        var icon = IconLoader.LoadFromFile(_appServices.IconGateway.ResolveProgramIcon(exe));
        if (icon != null)
        {
            vm.OriginalIcon = icon;
            vm.Icon = IconTinter.Apply(icon);
            return;
        }
        // Not cached yet — extract in the background and push when ready.
        // ExtractAndCacheBestIcon tries the exe resource, then the AppX
        // package logo, then (last, since only a running app has one) the
        // window's own icon.
        _ = Task.Run(() =>
        {
            try
            {
                string? cached = WindowsIconExtractor.ExtractAndCacheBestIcon(exe, windowHandle);
                var loaded = IconLoader.LoadFromFile(cached);
                if (loaded != null)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.OriginalIcon = loaded;
                        vm.Icon = IconTinter.Apply(loaded);
                    });
            }
            catch { /* best-effort */ }
        });
    }

    public void Shutdown()
    {
        if (_appServices != null)
            _appServices.LocalizationService.RemoveListener(UpdateDockUI);
        _indicatorCts?.Cancel();
        _indicatorCts?.Dispose();
        _indicatorCts = null;
        _attention?.Dispose();
        _attention = null;
    }
}
