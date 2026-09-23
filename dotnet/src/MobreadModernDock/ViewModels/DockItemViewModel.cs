namespace MobreadModernDock.ViewModels;

using System;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using MobreadModernDock.Core.Models;

/// <summary>
/// ViewModel for a single dock item. Holds the icon bitmap, localized label,
/// running-app indicator state, and click-to-launch command.
/// </summary>
public class DockItemViewModel : ViewModelBase
{
    private Bitmap? _icon;
    private bool _isRunning;

    /// <summary>The underlying dock item model.</summary>
    public DockItem Item { get; }

    /// <summary>Localized display label (shown in tooltip).</summary>
    public string Label { get; }

    /// <summary>The icon bitmap to render (loaded from cache or assets).</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>True when the program has at least one open window (drives indicator dot).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private bool _needsAttention;
    /// <summary>True while the app is flashing for attention; cleared on click or when the app takes focus.</summary>
    public bool NeedsAttention
    {
        get => _needsAttention;
        set
        {
            if (!SetProperty(ref _needsAttention, value)) return;
            _bounce ??= new BounceAnimator(o => BounceOffset = o);
            if (value) _bounce.Start(); else _bounce.Stop();
        }
    }

    /// <summary>A single macOS-style hop, used as launch feedback.</summary>
    public void BounceOnce()
    {
        _bounce ??= new BounceAnimator(o => BounceOffset = o);
        _bounce.BounceOnce();
    }

    private BounceAnimator? _bounce;
    private double _bounceOffset;
    /// <summary>Vertical hop applied to the icon while <see cref="NeedsAttention"/>.</summary>
    public double BounceOffset { get => _bounceOffset; set => SetProperty(ref _bounceOffset, value); }

    /// <summary>True if this item should show a running-app indicator (program items only).</summary>
    public bool ShowIndicator { get; }

    // --- Separator items ---

    /// <summary>True when this item is a user-placed divider rather than a launchable icon.</summary>
    public bool IsSeparator => Item is DockSeparatorItemModel;

    /// <summary>Inverse of <see cref="IsSeparator"/>, for binding the normal icon content.</summary>
    public bool IsNotSeparator => !IsSeparator;

    private bool _isVerticalDock;
    /// <summary>
    /// Dock orientation, pushed in at creation. A separator draws across the
    /// dock's minor axis: a vertical hairline in a horizontal dock, and a
    /// horizontal one in a vertical dock.
    /// </summary>
    public bool IsVerticalDock
    {
        get => _isVerticalDock;
        set
        {
            if (!SetProperty(ref _isVerticalDock, value)) return;
            NotifySeparatorSize();
        }
    }

    /// <summary>Thickness of the drawn line, in px.</summary>
    private const double SeparatorThickness = 2;

    /// <summary>
    /// Length of the line along the dock's minor axis: 70% of the icon size,
    /// so it reads as a divider between icons rather than a full-height bar.
    /// </summary>
    private double SeparatorLength => Math.Max(8, IconSize * 0.7);

    public double SeparatorWidth => IsVerticalDock ? SeparatorLength : SeparatorThickness;
    public double SeparatorHeight => IsVerticalDock ? SeparatorThickness : SeparatorLength;

    /// <summary>Blank space the separator reserves along the main axis, in px (0 for a plain hairline).</summary>
    private double SeparatorSpacingPx =>
        Item is DockSeparatorItemModel s ? Math.Round(DockSeparatorItemModel.SanitizeSpacing(s.Spacing) * IconSize) : 0;

    /// <summary>
    /// Size of the separator's cell: the hairline plus its blank spacing on
    /// the main axis, the line length on the minor axis. The hairline is
    /// centred inside it, so the gap is split evenly on both sides.
    /// </summary>
    public double SeparatorCellWidth => IsVerticalDock ? SeparatorLength : SeparatorThickness + SeparatorSpacingPx;
    public double SeparatorCellHeight => IsVerticalDock ? SeparatorThickness + SeparatorSpacingPx : SeparatorLength;

    /// <summary>False for an invisible spacer (separator with the line hidden).</summary>
    public bool ShowSeparatorLine => Item is not DockSeparatorItemModel s || !s.HideLine;

    private void NotifySeparatorSize()
    {
        OnPropertyChanged(nameof(SeparatorWidth));
        OnPropertyChanged(nameof(SeparatorHeight));
        OnPropertyChanged(nameof(SeparatorCellWidth));
        OnPropertyChanged(nameof(SeparatorCellHeight));
        OnPropertyChanged(nameof(ShowSeparatorLine));
    }

    /// <summary>The icon render size in pixels (mirrors the dock's IconsSize setting).</summary>
    private int _iconSize = 48;
    public int IconSize
    {
        get => _iconSize;
        // Assigned after construction by UpdateDockUI, so the separator's
        // computed dimensions must re-notify or they'd keep the 48px default.
        set
        {
            if (!SetProperty(ref _iconSize, value)) return;
            NotifySeparatorSize();
        }
    }

    /// <summary>The executable path for running-indicator polling (program items only).</summary>
    public string? ExecutablePath { get; }

    /// <summary>Command invoked when the dock item is clicked.</summary>
    public ICommand ClickCommand { get; }

    public DockItemViewModel(
        DockItem item,
        string label,
        ICommand clickCommand,
        bool showIndicator = false,
        string? executablePath = null)
    {
        Item = item;
        Label = label;
        ClickCommand = clickCommand;
        ShowIndicator = showIndicator;
        ExecutablePath = executablePath;
    }
}
