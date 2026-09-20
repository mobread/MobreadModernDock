namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>
/// Hides and restores the Windows taskbar (primary <c>Shell_TrayWnd</c> and
/// every <c>Shell_SecondaryTrayWnd</c>). There is no supported API for this;
/// the approach used by dock replacements is to hide the taskbar windows and
/// flip the appbar to auto-hide so the work area grows into the freed space.
///
/// Restoration must be unconditional: <see cref="Restore"/> is safe to call
/// when nothing was hidden and is invoked from app shutdown, from a
/// process-exit handler, and at the next startup (covers an unclean exit).
/// </summary>
public static class TaskbarVisibility
{
    private static readonly object Sync = new();
    private static bool _hidden;
    private static bool _exitHookInstalled;

    public static bool IsHidden { get { lock (Sync) return _hidden; } }

    public static void Apply(bool hide)
    {
        if (hide) Hide(); else Restore();
    }

    public static void Hide()
    {
        lock (Sync)
        {
            InstallExitHook();
            // Auto-hide first so the shell releases the reserved work area,
            // then hide the windows so the auto-hide sliver never shows.
            SetAppBarAutoHide(true);
            HideAllTaskbarWindows();
            _hidden = true;
            StartEnforcer();
        }
    }

    private static void HideAllTaskbarWindows()
    {
        foreach (var hwnd in FindTaskbars())
            User32.ShowWindow(hwnd, Win32Constants.SW_HIDE);
    }

    private static Timer? _enforcer;

    /// <summary>
    /// The shell re-creates the secondary-monitor taskbars (new HWNDs) when
    /// the appbar state changes and on display-layout changes, so a single
    /// SW_HIDE does not stick. A low-frequency enforcer re-hides any taskbar
    /// window that reappears while the setting is on. Suspended while the
    /// taskbar is temporarily shown for a tray-icon click.
    /// </summary>
    private static void StartEnforcer()
    {
        _enforcer ??= new Timer(_ =>
        {
            lock (Sync)
            {
                if (!_hidden || _temporarilyShown) return;
                foreach (var hwnd in FindTaskbars())
                    if (User32.IsWindowVisible(hwnd))
                        User32.ShowWindow(hwnd, Win32Constants.SW_HIDE);
            }
        }, null, 250, 750);
    }

    private static void StopEnforcer()
    {
        _enforcer?.Dispose();
        _enforcer = null;
    }

    public static void Restore()
    {
        lock (Sync)
        {
            StopEnforcer();
            _hidden = false;
            _temporarilyShown = false;
            foreach (var hwnd in FindTaskbars())
                User32.ShowWindow(hwnd, Win32Constants.SW_SHOW);
            SetAppBarAutoHide(false);
        }
    }

    private static bool _temporarilyShown;

    /// <summary>
    /// Makes the taskbar windows visible without changing the "hidden"
    /// setting state or the appbar (work area stays expanded). Used to let a
    /// synthesized click reach a tray icon while the taskbar is hidden.
    /// </summary>
    public static void ShowTemporarily()
    {
        lock (Sync)
        {
            if (!_hidden) return;
            foreach (var hwnd in FindTaskbars())
                User32.ShowWindow(hwnd, Win32Constants.SW_SHOWNOACTIVATE);
            _temporarilyShown = true;
        }
    }

    /// <summary>Undoes <see cref="ShowTemporarily"/> if the taskbar is still meant to be hidden.</summary>
    public static void RehideIfTemporarilyShown()
    {
        lock (Sync)
        {
            if (!_temporarilyShown) return;
            _temporarilyShown = false;
            if (!_hidden) return;
            foreach (var hwnd in FindTaskbars())
                User32.ShowWindow(hwnd, Win32Constants.SW_HIDE);
        }
    }

    /// <summary>
    /// Restores the taskbar if a previous run left it hidden. Cheap: it only
    /// touches the shell when a taskbar window is actually invisible.
    /// </summary>
    public static void RestoreIfLeftHidden()
    {
        foreach (var hwnd in FindTaskbars())
        {
            if (!User32.IsWindowVisible(hwnd))
            {
                Restore();
                return;
            }
        }
    }

    private static void InstallExitHook()
    {
        if (_exitHookInstalled) return;
        _exitHookInstalled = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Restore(); } catch { } };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { try { Restore(); } catch { } };
    }

    private static List<IntPtr> FindTaskbars()
    {
        var list = new List<IntPtr>();
        IntPtr primary = User32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) list.Add(primary);

        User32.EnumWindows((hWnd, _) =>
        {
            var cls = new StringBuilder(64);
            User32.GetClassName(hWnd, cls, cls.Capacity);
            if (cls.ToString() == "Shell_SecondaryTrayWnd")
                list.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static void SetAppBarAutoHide(bool autoHide)
    {
        IntPtr tray = User32.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        var abd = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = tray,
            lParam = autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP,
        };
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    private const uint ABM_SETSTATE = 0x0000000A;
    private const int ABS_AUTOHIDE = 0x1;
    private const int ABS_ALWAYSONTOP = 0x2;

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
