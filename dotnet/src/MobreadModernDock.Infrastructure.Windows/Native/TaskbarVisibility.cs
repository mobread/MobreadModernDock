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
            HideOne(hwnd);
    }

    /// <summary>
    /// SW_HIDE plus a fully transparent layered alpha. The shell re-shows the
    /// taskbar for a frame or two on every appbar re-layout (see
    /// <see cref="StartEnforcer"/>) and no re-hide, however fast, beats the
    /// compositor to it; with alpha 0 those frames paint nothing. The
    /// taskbar accepts layered attributes from another process, and the
    /// shell does not reset them when it shows the window.
    /// </summary>
    private static void HideOne(IntPtr hwnd)
    {
        MakeTransparent(hwnd, true);
        User32.ShowWindow(hwnd, Win32Constants.SW_HIDE);
    }

    private static void MakeTransparent(IntPtr hwnd, bool transparent)
    {
        var ex = User32.GetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE).ToInt64();
        bool layered = (ex & Win32Constants.WS_EX_LAYERED) != 0;
        if (transparent)
        {
            if (!layered)
                User32.SetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE, new IntPtr(ex | Win32Constants.WS_EX_LAYERED));
            User32.SetLayeredWindowAttributes(hwnd, 0, 0, Win32Constants.LWA_ALPHA);
        }
        else if (layered)
        {
            // Opaque again, then drop the style: the taskbar was not layered
            // to begin with, and leaving it so changes how the shell paints it.
            User32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32Constants.LWA_ALPHA);
            User32.SetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE, new IntPtr(ex & ~(long)Win32Constants.WS_EX_LAYERED));
        }
    }

    private static Timer? _enforcer;
    private static Thread? _hookThread;
    private static uint _hookThreadId;

    /// <summary>
    /// The shell re-shows the taskbars whenever it re-lays out its appbars -
    /// on every <c>ABM_SETPOS</c> from any appbar (ours included, on each dock
    /// resize), on display changes, and it re-creates the secondary-monitor
    /// taskbars (new HWNDs) after appbar state changes - so a single
    /// <c>SW_HIDE</c> does not stick.
    ///
    /// Two layers keep them hidden. A <c>WinEvent</c> hook on
    /// <c>EVENT_OBJECT_SHOW</c> re-hides a taskbar the moment the shell shows
    /// it. The hook runs on its own thread with nothing but a message pump:
    /// out-of-context WinEvents are delivered through the hooking thread's
    /// queue, and on the UI thread they queued behind the very layout work
    /// that caused the SETPOS, adding ~20 ms during which a frame of taskbar
    /// was presented. The polling timer below is the fallback for anything
    /// the hook misses (it was the only mechanism before, and its 750 ms
    /// period was exactly the flicker users saw on the secondary monitor).
    /// Both stand down while the taskbar is temporarily shown for a tray-icon
    /// click.
    /// </summary>
    private static void StartEnforcer()
    {
        if (_hookThread == null)
        {
            _hookThread = new Thread(HookThreadMain) { IsBackground = true, Name = "TaskbarHideHook" };
            _hookThread.Start();
        }
        _enforcer ??= new Timer(_ =>
        {
            lock (Sync)
            {
                if (!_hidden || _temporarilyShown) return;
                foreach (var hwnd in FindTaskbars())
                    if (User32.IsWindowVisible(hwnd))
                        HideOne(hwnd);
            }
        }, null, 250, 750);
    }

    private static void HookThreadMain()
    {
        _hookThreadId = Kernel32.GetCurrentThreadId();
        // Local so the delegate lives as long as the loop; the hook holds a raw pointer to it.
        WinEventProc proc = OnWinEvent;
        IntPtr hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero,
            proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        try
        {
            while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                User32.TranslateMessage(ref msg);
                User32.DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            GC.KeepAlive(proc);
        }
    }

    private static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        // Cheap class check first: this fires for every window shown on the desktop.
        var cls = new StringBuilder(64);
        User32.GetClassName(hwnd, cls, cls.Capacity);
        string name = cls.ToString();
        if (name != "Shell_TrayWnd" && name != "Shell_SecondaryTrayWnd") return;
        lock (Sync)
        {
            if (!_hidden || _temporarilyShown) return;
            // A re-created secondary taskbar is a new HWND with no alpha yet.
            HideOne(hwnd);
        }
    }

    private static void StopEnforcer()
    {
        _enforcer?.Dispose();
        _enforcer = null;
        if (_hookThread != null)
        {
            User32.PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread = null;
        }
    }

    public static void Restore()
    {
        lock (Sync)
        {
            StopEnforcer();
            _hidden = false;
            _temporarilyShown = false;
            foreach (var hwnd in FindTaskbars())
            {
                MakeTransparent(hwnd, false);
                User32.ShowWindow(hwnd, Win32Constants.SW_SHOW);
            }
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
            // Flag first: the SW_SHOW below raises EVENT_OBJECT_SHOW, and the
            // hook must not undo it.
            _temporarilyShown = true;
            foreach (var hwnd in FindTaskbars())
            {
                MakeTransparent(hwnd, false);
                User32.ShowWindow(hwnd, Win32Constants.SW_SHOWNOACTIVATE);
            }
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
                HideOne(hwnd);
        }
    }

    /// <summary>
    /// Restores the taskbar if a previous run left it hidden or transparent.
    /// Cheap: it only touches the shell when a taskbar window is actually
    /// invisible.
    /// </summary>
    public static void RestoreIfLeftHidden()
    {
        foreach (var hwnd in FindTaskbars())
        {
            bool layered = (User32.GetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE).ToInt64() & Win32Constants.WS_EX_LAYERED) != 0;
            if (!User32.IsWindowVisible(hwnd) || layered)
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

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint WM_QUIT = 0x0012;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod,
        WinEventProc proc, uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

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
