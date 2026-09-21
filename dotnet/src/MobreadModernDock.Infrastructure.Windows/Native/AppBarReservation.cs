namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using MobreadModernDock.Core.Application;

/// <summary>
/// #4 "Use it as a taskbar": reserves the screen edge the dock occupies via
/// <c>SHAppBarMessage</c>, so maximized windows stop at the dock instead of
/// sliding underneath it.
///
/// The appbar is registered on a <b>separate hidden top-level window</b>, not
/// on the dock itself. The dock is normally re-parented to the desktop
/// (WorkerW) and carries WS_EX_NOACTIVATE — the shell's appbar bookkeeping
/// expects a plain unowned top-level window, and registering the dock would
/// also tie reservation to the always-on-top toggle. The hidden window is
/// moved to the reserved strip (as the docs prescribe) but never painted.
///
/// Removal must be unconditional and belt-and-braces: a reservation that
/// outlives the process leaves the user's work area permanently shrunk with
/// no UI to undo it. <see cref="Clear"/> is safe to call when nothing is
/// registered and is also wired to ProcessExit / UnhandledException.
/// </summary>
public sealed class AppBarReservation : IDisposable
{
    private const uint ABM_NEW = 0x00000000;
    private const uint ABM_REMOVE = 0x00000001;
    private const uint ABM_QUERYPOS = 0x00000002;
    private const uint ABM_SETPOS = 0x00000003;

    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

    private const int ABN_POSCHANGED = 0x0000001;

    private const uint WM_APPBAR_CALLBACK = 0x0400 + 0x0501; // WM_USER + 0x501

    private const string ClassName = "MobreadDockAppBarWindow";
    private static readonly IntPtr HInstance = Kernel32.GetModuleHandle(null);
    private static readonly WndProc WndProcDelegate = WndProcImpl; // class lifetime; never collected
    private static AppBarReservation? _instance;
    private static bool _classRegistered;

    private readonly object _sync = new();
    private IntPtr _hwnd;
    private bool _registered;
    private ScreenEdge _edge = ScreenEdge.None;
    private ScreenBounds? _strip;
    private bool _exitHookInstalled;
    private bool _reentrant; // guards the ABN_POSCHANGED -> SetPos -> ABN_POSCHANGED loop

    /// <summary>The strip currently reserved, or null when nothing is reserved.</summary>
    public ScreenBounds? Current { get { lock (_sync) return _strip; } }

    /// <summary>
    /// Reserves <paramref name="strip"/> on <paramref name="edge"/>. Passing
    /// <see cref="ScreenEdge.None"/> or a null strip releases the reservation.
    /// Repeated calls with an unchanged strip are cheap no-ops — this is
    /// called from layout and drag paths that fire continuously.
    /// </summary>
    public void Apply(ScreenEdge edge, ScreenBounds? strip)
    {
        lock (_sync)
        {
            if (edge == ScreenEdge.None || strip is null)
            {
                ClearCore();
                return;
            }

            if (_registered && edge == _edge && !DockReservation.Differs(_strip, strip))
                return;

            EnsureWindow();
            if (_hwnd == IntPtr.Zero) return;

            if (!_registered)
            {
                var add = NewData();
                add.uCallbackMessage = WM_APPBAR_CALLBACK;
                if (SHAppBarMessage(ABM_NEW, ref add) == UIntPtr.Zero) return;
                _registered = true;
                InstallExitHook();
            }

            _edge = edge;
            _strip = SetPos(edge, strip.Value);
        }
    }

    /// <summary>Releases the reservation and restores the work area. Safe when nothing is reserved.</summary>
    public void Clear()
    {
        lock (_sync) ClearCore();
    }

    private void ClearCore()
    {
        if (!_registered)
        {
            _edge = ScreenEdge.None;
            _strip = null;
            return;
        }
        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
        _edge = ScreenEdge.None;
        _strip = null;
    }

    /// <summary>
    /// ABM_QUERYPOS + ABM_SETPOS. The shell may shrink the proposal (another
    /// appbar already owns part of that edge, e.g. the real taskbar when it is
    /// not hidden); the adjusted rect it returns is the truth, so it is what we
    /// record and where the hidden window is placed.
    /// </summary>
    private ScreenBounds SetPos(ScreenEdge edge, ScreenBounds strip)
    {
        var abd = NewData();
        abd.uEdge = ToNativeEdge(edge);
        abd.rc = ToRect(strip);

        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // QUERYPOS only adjusts the axis it owns; restore the full span on the
        // other axis so a partial answer cannot shrink the bar's length.
        var full = ToRect(strip);
        if (edge is ScreenEdge.Left or ScreenEdge.Right)
        {
            abd.rc.Top = full.Top;
            abd.rc.Bottom = full.Bottom;
            // Keep the requested thickness anchored to the edge.
            if (edge == ScreenEdge.Left) abd.rc.Right = abd.rc.Left + (full.Right - full.Left);
            else abd.rc.Left = abd.rc.Right - (full.Right - full.Left);
        }
        else
        {
            abd.rc.Left = full.Left;
            abd.rc.Right = full.Right;
            if (edge == ScreenEdge.Top) abd.rc.Bottom = abd.rc.Top + (full.Bottom - full.Top);
            else abd.rc.Top = abd.rc.Bottom - (full.Bottom - full.Top);
        }

        SHAppBarMessage(ABM_SETPOS, ref abd);

        User32.SetWindowPos(_hwnd, IntPtr.Zero,
            abd.rc.Left, abd.rc.Top,
            abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
            Win32Constants.SWP_NOZORDER | Win32Constants.SWP_NOACTIVATE);

        return new ScreenBounds(abd.rc.Left, abd.rc.Top,
            abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top);
    }

    /// <summary>
    /// The shell asks every appbar to re-assert its position when the taskbar
    /// moves, auto-hide flips or the display layout changes. Without this the
    /// reservation is silently dropped and windows maximize over the dock.
    /// </summary>
    private void OnPosChanged()
    {
        lock (_sync)
        {
            if (!_registered || _reentrant || _edge == ScreenEdge.None || _strip is null) return;
            _reentrant = true;
            try { _strip = SetPos(_edge, _strip.Value); }
            finally { _reentrant = false; }
        }
    }

    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero && User32.IsWindow(_hwnd)) return;

        _instance = this;
        if (!_classRegistered)
        {
            User32.RegisterClass(new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = HInstance,
                lpszClassName = ClassName,
            });
            _classRegistered = true;
        }

        // A plain top-level window: HWND_MESSAGE windows are not valid appbars.
        // WS_EX_TOOLWINDOW keeps it out of Alt-Tab; it is never shown.
        _hwnd = User32.CreateWindowEx(
            Win32Constants.WS_EX_TOOLWINDOW | Win32Constants.WS_EX_NOACTIVATE,
            ClassName, "", Win32Constants.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, HInstance, IntPtr.Zero);
    }

    private static IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APPBAR_CALLBACK && _instance != null && wParam.ToInt32() == ABN_POSCHANGED)
        {
            try { _instance.OnPosChanged(); } catch { }
            return IntPtr.Zero;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void InstallExitHook()
    {
        if (_exitHookInstalled) return;
        _exitHookInstalled = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Clear(); } catch { } };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { try { Clear(); } catch { } };
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    private static uint ToNativeEdge(ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => ABE_LEFT,
        ScreenEdge.Top => ABE_TOP,
        ScreenEdge.Right => ABE_RIGHT,
        _ => ABE_BOTTOM,
    };

    private static RECT ToRect(ScreenBounds b) => new()
    {
        Left = (int)Math.Round(b.MinX),
        Top = (int)Math.Round(b.MinY),
        Right = (int)Math.Round(b.MaxX),
        Bottom = (int)Math.Round(b.MaxY),
    };

    public void Dispose()
    {
        lock (_sync)
        {
            ClearCore();
            if (_hwnd != IntPtr.Zero)
            {
                User32.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
