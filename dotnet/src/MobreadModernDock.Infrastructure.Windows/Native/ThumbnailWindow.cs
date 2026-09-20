using System;
using System.Runtime.InteropServices;

namespace MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// A small borderless, click-through top-level window that hosts a single
/// DWM thumbnail. DWM thumbnails composite above the host window's own content
/// and ignore window regions, so rounded corners are achieved with
/// DWMWA_WINDOW_CORNER_PREFERENCE (Win11 DWM corner rounding), which clips the
/// thumbnail at the composition level.
/// </summary>
public sealed class ThumbnailWindow : IDisposable
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const string ClassName = "MobreadDockThumbnailWindow";

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    private static readonly IntPtr _hInstance = Kernel32.GetModuleHandle(null);

    private static readonly WndProc WndProcImpl = (hwnd, msg, wParam, lParam) =>
    {
        if (msg == WM_NCHITTEST)
            return new IntPtr(HTTRANSPARENT);
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    };

    private static readonly nint WndProcPtr = Marshal.GetFunctionPointerForDelegate(WndProcImpl);

    private readonly IntPtr _hwnd;
    private IntPtr _thumb;
    private int _x, _y, _width, _height;

    public IntPtr Handle => _hwnd;

    /// <summary>Current screen rect in physical pixels (updated by ctor and Move).</summary>
    public (int X, int Y, int Width, int Height) ScreenRect => (_x, _y, _width, _height);

    /// <summary>True when the physical screen point is inside this window's rect.</summary>
    public bool ContainsPoint(int x, int y) =>
        x >= _x && x < _x + _width && y >= _y && y < _y + _height;

    static ThumbnailWindow()
    {
        var wc = new WNDCLASS
        {
            lpfnWndProc = WndProcPtr,
            hInstance = _hInstance,
            lpszClassName = ClassName,
            hbrBackground = IntPtr.Zero
        };
        User32.RegisterClass(wc);
    }

    /// <summary>
    /// Creates a click-through thumbnail window for <paramref name="sourceHwnd"/>,
    /// with no DWM border. <paramref name="rounding"/> is the desired corner radius
    /// in pixels (same value the popup rows use): DWM clips the thumbnail at the
    /// nearest supported preset (square, small ~4px or standard ~8px), so very
    /// small dock roundings yield nearly square previews instead of the fixed 8px.
    /// </summary>
    public ThumbnailWindow(IntPtr sourceHwnd, int x, int y, int width, int height, int rounding = 8)
    {
        _x = x; _y = y; _width = width; _height = height;
        _hwnd = User32.CreateWindowEx(
            (uint)(Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_NOACTIVATE),
            ClassName, "",
            (uint)(Win32Constants.WS_POPUP | Win32Constants.WS_VISIBLE),
            x, y, width, height, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create thumbnail window");

        int corner = rounding switch
        {
            <= 2 => DWMWCP_DONOTROUND,
            <= 5 => DWMWCP_ROUNDSMALL,
            _ => DWMWCP_ROUND
        };
        DwmThumbnailInterop.SetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, corner);

        if (!DwmThumbnailInterop.Register(_hwnd, sourceHwnd, out _thumb))
            throw new InvalidOperationException("Failed to register DWM thumbnail");

        var rect = new DwmThumbnailInterop.RECT { Left = 0, Top = 0, Right = width, Bottom = height };
        DwmThumbnailInterop.Update(_thumb, rect);

        User32.SetWindowPos(_hwnd, Win32Constants.HWND_TOPMOST, x, y, width, height, 0);
    }

    public void Move(int x, int y)
    {
        _x = x; _y = y;
        User32.SetWindowPos(_hwnd, Win32Constants.HWND_TOPMOST, x, y, 0, 0,
            Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Sets the window's per-window alpha (0..255) via layered attributes, used
    /// to fade thumbnails in/out in sync with the popup window.
    /// </summary>
    public void SetOpacity(byte alpha)
    {
        IntPtr exStylePtr = User32.GetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE);
        int exStyle = exStylePtr.ToInt32();
        if ((exStyle & Win32Constants.WS_EX_LAYERED) == 0)
        {
            User32.SetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE,
                new IntPtr(exStyle | Win32Constants.WS_EX_LAYERED));
        }
        User32.SetLayeredWindowAttributes(_hwnd, 0, alpha, Win32Constants.LWA_ALPHA);
    }

    public void Dispose()
    {
        if (_thumb != IntPtr.Zero)
        {
            DwmThumbnailInterop.Unregister(_thumb);
            _thumb = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
            User32.DestroyWindow(_hwnd);
    }
}
