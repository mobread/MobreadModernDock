namespace MobreadModernDock.ViewModels;

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

    private BounceAnimator? _bounce;
    private double _bounceOffset;
    /// <summary>Vertical hop applied to the icon while <see cref="NeedsAttention"/>.</summary>
    public double BounceOffset { get => _bounceOffset; set => SetProperty(ref _bounceOffset, value); }

    /// <summary>True if this item should show a running-app indicator (program items only).</summary>
    public bool ShowIndicator { get; }

    /// <summary>The icon render size in pixels (mirrors the dock's IconsSize setting).</summary>
    public int IconSize { get; set; } = 48;

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
