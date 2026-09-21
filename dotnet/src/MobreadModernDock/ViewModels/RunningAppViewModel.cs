using Avalonia.Media.Imaging;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// A program that is running with open window(s) but is NOT pinned to the dock.
/// Shown in the running-apps section (right of the separator), mirroring the
/// Windows taskbar. Hovering shows the window-preview popup; there is no click action.
/// </summary>
public class RunningAppViewModel : ViewModelBase
{
    private Bitmap? _icon;
    private bool _isRunning;

    /// <summary>The owning executable path (used to group windows and load the icon).</summary>
    public string ExecutablePath { get; }

    /// <summary>Display label for the tooltip (fallback to executable name).</summary>
    public string Label { get; }

    /// <summary>The icon bitmap to render (tinted when the icon tint is enabled).</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>The untinted icon bitmap, kept so the tint can be re-applied
    /// when the tint color or toggle changes (these VMs persist between refreshes).</summary>
    public Bitmap? OriginalIcon { get; set; }

    /// <summary>True while the app has at least one open window.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private bool _needsAttention;
    /// <summary>True while the app is flashing for attention.</summary>
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
    public double BounceOffset { get => _bounceOffset; set => SetProperty(ref _bounceOffset, value); }

    /// <summary>The icon render size in pixels (mirrors the dock's IconsSize setting).</summary>
    private int _iconSize = 48;
    public int IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public RunningAppViewModel(string executablePath, string label)
    {
        ExecutablePath = executablePath;
        Label = label;
        IsRunning = true;
    }
}
