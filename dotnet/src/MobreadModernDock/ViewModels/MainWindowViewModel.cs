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
/// and the running-app indicator watcher. Direct port of DockController's
/// state and update logic, expressed as MVVM bindings.
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
                NotifyGridShape();
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
    // The pinned ItemsControl uses a UniformGrid. For a horizontal dock the
    // configured value is the number of rows and columns follow from the item
    // count; for a vertical dock it is the number of columns.
    private int _dockLines = 1;
    public int DockLines
    {
        get => _dockLines;
        set
        {
            if (SetProperty(ref _dockLines, Math.Max(1, value)))
                NotifyGridShape();
        }
    }

    public int GridRows => IsVerticalDock ? CeilDiv(Items.Count, DockLines) : DockLines;
    public int GridColumns => IsVerticalDock ? DockLines : CeilDiv(Items.Count, DockLines);

    /// <summary>
    /// Half the configured spacing on every side of each pinned item, so
    /// neighbours in the UniformGrid are exactly <see cref="Spacing"/> apart
    /// in both directions.
    /// </summary>
    public Thickness CellMargin => new(Spacing / 2.0);

    private static int CeilDiv(int a, int b) => b <= 0 ? a : (a + b - 1) / b;

    private void NotifyGridShape()
    {
        OnPropertyChanged(nameof(GridRows));
        OnPropertyChanged(nameof(GridColumns));
    }

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
    public IBrush DockBackground { get => _dockBackground; set => SetProperty(ref _dockBackground, value); }

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
        UpdateDockUI();
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
        UpdateDockUI();
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
        UpdateDockUI();
        Task.Run(RefreshRunningApps);
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
        NotifyGridShape();
        ApplyAppearance();
        RepositionAction?.Invoke();
        PreviewDismissAction?.Invoke();
        LayerRefreshAction?.Invoke();
        App.RefreshWidgetAppearance();
        if (!IsMirrorViewModel) App.SyncMirrorDocks();
    }

    /// <summary>#10 Set on VMs that back a secondary-monitor mirror so they don't recurse into SyncMirrorDocks.</summary>
    public bool IsMirrorViewModel { get; init; }
    // --- continued below ---

    private DockItemViewModel? CreateItemViewModel(DockItem item, LocalizationService loc)
    {
        string label = loc.DockItemLabel(item);

        if (item is DockSettingsItemModel)
        {
            var icon = IconLoader.LoadFromAsset(IconLoader.MapResourcePath(item.Path));
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockWindowsModuleItemModel moduleItem)
        {
            var icon = IconLoader.LoadWindowsModuleIcon(moduleItem.Module);
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockProgramItemModel programItem)
        {
            string? iconPath = _appServices!.IconGateway.ResolveProgramIcon(programItem.ExecutablePath);
            var icon = IconLoader.LoadFromFile(iconPath);
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
            var icon = IconLoader.LoadFromFile(iconPath);
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
        foreach (var app in RunningApps)
            app.IconSize = IconsSize;
        Spacing = appearance.GetSpacingBetweenIcons();
        DockLines = appearance.GetDockRows();
        WindowOpacity = appearance.GetGlobalOpacityPercentage() / 100.0;
        BorderRounding = appearance.GetDockBorderRounding();

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
            ClearAttention(itemVm.ExecutablePath);
            if (itemVm.Item is DockFolderItemModel
                && _appServices.AppearanceService.GetFolderStacks()
                && ShowFolderStackAction?.Invoke(itemVm) == true)
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
        // Modern/UWP apps (Settings, etc.) carry no icon resource in the EXE
        // and their frame window exposes none either; fall back to the running
        // window's icon, then to the AppX package manifest logo.
        _ = Task.Run(() =>
        {
            try
            {
                _appServices.IconGateway.CacheProgramIcon(exe);
                string? cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                var loaded = IconLoader.LoadFromFile(cached);
                if (loaded == null && windowHandle != IntPtr.Zero)
                {
                    WindowsIconExtractor.ExtractAndCacheWindowIcon(exe, windowHandle);
                    cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    loaded = IconLoader.LoadFromFile(cached);
                }
                if (loaded == null)
                {
                    WindowsIconExtractor.ExtractAndCacheAppxIcon(exe);
                    cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    loaded = IconLoader.LoadFromFile(cached);
                }
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
