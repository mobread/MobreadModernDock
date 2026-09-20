using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// A small translucent, circular native button shown on top of a window
/// preview thumbnail's top-right corner (taskbar-style). It lives in its own
/// top-level window because the live DWM thumbnail composites above the
/// Avalonia popup content, so an Avalonia control could never render over it.
/// The window is clipped to a perfect circle via a window region and made
/// translucent with a whole-window alpha (LWA_ALPHA); WM_PAINT draws the dark
/// circle and the white X with GDI+.
/// </summary>
public sealed class PreviewCloseButtonWindow : IDisposable
{
    public const int Size = 22;

    private const string ClassName = "MobreadDockCloseButtonWindow";
    private const int WM_ERASEBKGND = 0x0014;

    // Peak whole-window alpha applied via LWA_ALPHA.
    private const int AlphaMax = 200;
    // Fade-in/out duration and tick, mirroring the popup's own fade so the
    // button appears/disappears smoothly instead of popping.
    private const double FadeMilliseconds = 120;
    private const double FadeTickMilliseconds = 12;

    // IDC_HAND (32649): pointer cursor used while hovering the button.
    private static readonly IntPtr HandCursor = User32.LoadCursor(IntPtr.Zero, new IntPtr(32649));

    private static readonly IntPtr _hInstance = Kernel32.GetModuleHandle(null);

    private static readonly WndProc WndProcImpl = (hwnd, msg, wParam, lParam) =>
    {
        var self = GetInstance(hwnd);
        switch (msg)
        {
            case (uint)Win32Constants.WM_PAINT:
                self?.OnPaint();
                return IntPtr.Zero;
            case (uint)Win32Constants.WM_ERASEBKGND:
                // Nothing to erase: the region clips to the circle and WM_PAINT
                // covers it. Returning 1 avoids the default black fill flicker.
                return new IntPtr(1);
            case (uint)Win32Constants.WM_MOUSEMOVE:
                User32.SetCursor(HandCursor);
                self?.BeginMouseLeaveTracking();
                return IntPtr.Zero;
            case (uint)Win32Constants.WM_MOUSELEAVE:
                self?.OnPointerExited();
                return IntPtr.Zero;
            case (uint)Win32Constants.WM_LBUTTONUP:
                self?.OnClicked();
                return IntPtr.Zero;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    };

    private static readonly nint WndProcPtr = Marshal.GetFunctionPointerForDelegate(WndProcImpl);

    private readonly IntPtr _hwnd;
    private int _x, _y;
    private bool _mouseLeaveTracked;
    private bool _disposed;
    private readonly object _fadeLock = new();
    private System.Threading.Timer? _fadeTimer;
    private bool _fadeIn;
    private double _fadeValue;

    /// <summary>Raised when the button is clicked.</summary>
    public Action? Clicked { get; set; }

    /// <summary>Raised when the pointer leaves the button.</summary>
    public Action? PointerExited { get; set; }

    public IntPtr Handle => _hwnd;

    /// <summary>Current screen rect in physical pixels.</summary>
    public (int X, int Y, int Width, int Height) ScreenRect => (_x, _y, Size, Size);

    /// <summary>True when the physical screen point is inside this window's rect.</summary>
    public bool ContainsPoint(int x, int y) =>
        x >= _x && x < _x + Size && y >= _y && y < _y + Size;

    static PreviewCloseButtonWindow()
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

    /// <summary>Creates a hidden close button at the given screen position.</summary>
    public PreviewCloseButtonWindow(int x, int y)
    {
        _x = x;
        _y = y;
        _hwnd = User32.CreateWindowEx(
            (uint)(Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_NOACTIVATE | Win32Constants.WS_EX_LAYERED),
            ClassName, "",
            (uint)Win32Constants.WS_POPUP,
            x, y, Size, Size, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create close button window");

        SetInstance(_hwnd, this);

        // Clip the window to a perfect circle: everything outside the circle is
        // not part of the window, so no square background can ever show. The
        // system takes ownership of the region handle.
        IntPtr region = User32.CreateEllipticRgn(0, 0, Size, Size);
        User32.SetWindowRgn(_hwnd, region, true);

        // Start fully transparent so the first Show() fades in from nothing.
        User32.SetLayeredWindowAttributes(_hwnd, 0, 0, Win32Constants.LWA_ALPHA);

        // The button never appears in the taskbar/Alt-Tab and stays on top of
        // the thumbnails.
        User32.SetWindowPos(_hwnd, Win32Constants.HWND_TOPMOST, x, y, 0, 0,
            Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE);
        User32.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    /// <summary>Fades the button in (from transparent to full opacity).</summary>
    public void Show()
    {
        lock (_fadeLock)
        {
            StopFade();
            User32.ShowWindow(_hwnd, Win32Constants.SW_SHOW);
            if (_fadeValue >= AlphaMax)
                return;
            _fadeIn = true;
            StartFade();
        }
    }

    /// <summary>Fades the button out, then hides it.</summary>
    public void Hide()
    {
        lock (_fadeLock)
        {
            StopFade();
            if (_fadeValue <= 0)
            {
                User32.ShowWindow(_hwnd, Win32Constants.SW_HIDE);
                return;
            }
            _fadeIn = false;
            StartFade();
        }
    }

    private void StartFade()
    {
        _fadeTimer = new System.Threading.Timer(OnFadeTick, null,
            TimeSpan.FromMilliseconds(FadeTickMilliseconds),
            TimeSpan.FromMilliseconds(FadeTickMilliseconds));
    }

    private void StopFade()
    {
        _fadeTimer?.Dispose();
        _fadeTimer = null;
    }

    private void OnFadeTick(object? state)
    {
        lock (_fadeLock)
        {
            double step = AlphaMax * (FadeTickMilliseconds / FadeMilliseconds);
            _fadeValue = _fadeIn
                ? Math.Min(AlphaMax, _fadeValue + step)
                : Math.Max(0, _fadeValue - step);
            User32.SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(_fadeValue), Win32Constants.LWA_ALPHA);

            bool finished = _fadeIn ? _fadeValue >= AlphaMax : _fadeValue <= 0;
            if (finished)
            {
                StopFade();
                if (!_fadeIn)
                    User32.ShowWindow(_hwnd, Win32Constants.SW_HIDE);
            }
        }
    }

    /// <summary>Moves the button to the given screen position.</summary>
    public void Move(int x, int y)
    {
        _x = x;
        _y = y;
        User32.SetWindowPos(_hwnd, Win32Constants.HWND_TOPMOST, x, y, 0, 0,
            Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE);
    }

    private void BeginMouseLeaveTracking()
    {
        if (_mouseLeaveTracked) return;
        _mouseLeaveTracked = true;
        var tme = new TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = Win32Constants.TME_LEAVE,
            hwndTrack = _hwnd,
            dwHoverTime = 0
        };
        User32.TrackMouseEvent(ref tme);
    }

    private void OnPointerExited()
    {
        _mouseLeaveTracked = false;
        PointerExited?.Invoke();
    }

    private void OnClicked() => Clicked?.Invoke();

    /// <summary>
    /// Draws the button as a dark circle with a white X directly into the
    /// window DC. The window region already clips to the circle, so the whole
    /// client can be painted safely.
    /// </summary>
    private void OnPaint()
    {
        User32.BeginPaint(_hwnd, out PAINTSTRUCT ps);
        try
        {
            using var g = Graphics.FromHdc(ps.hdc);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var circle = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
            g.FillEllipse(circle, 1, 1, Size - 3, Size - 3);

            using var pen = new Pen(Color.White, 1.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            // Center the X on the circle's own center so it never drifts off-axis.
            float c = (Size - 1) / 2f;
            float half = (Size - 7f) / 4f;
            g.DrawLine(pen, c - half, c - half, c + half, c + half);
            g.DrawLine(pen, c + half, c - half, c - half, c + half);
        }
        finally
        {
            User32.EndPaint(_hwnd, ref ps);
        }
    }

    private static PreviewCloseButtonWindow? GetInstance(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        IntPtr ptr = User32.GetWindowLongPtr(hwnd, Win32Constants.GWLP_USERDATA);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var handle = GCHandle.FromIntPtr(ptr);
            return handle.IsAllocated ? (PreviewCloseButtonWindow?)handle.Target : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void SetInstance(IntPtr hwnd, PreviewCloseButtonWindow instance)
    {
        IntPtr ptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
        User32.SetWindowLongPtr(hwnd, Win32Constants.GWLP_USERDATA, ptr);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_fadeLock)
        {
            StopFade();
        }
        IntPtr ptr = User32.GetWindowLongPtr(_hwnd, Win32Constants.GWLP_USERDATA);
        if (ptr != IntPtr.Zero)
        {
            try
            {
                GCHandle.FromIntPtr(ptr).Free();
            }
            catch (InvalidOperationException)
            {
            }
            User32.SetWindowLongPtr(_hwnd, Win32Constants.GWLP_USERDATA, IntPtr.Zero);
        }
        if (_hwnd != IntPtr.Zero)
            User32.DestroyWindow(_hwnd);
    }
}
