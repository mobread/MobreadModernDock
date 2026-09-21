namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// #13 Listens for taskbar-flash requests (an app calling FlashWindowEx to ask
/// for attention) via the shell hook and reports the owning executable + HWND.
/// A hidden message-only window receives WM_SHELLHOOKMESSAGE / HSHELL_FLASH.
/// </summary>
public sealed class AttentionMonitor : IDisposable
{
    private const int HSHELL_REDRAW = 6;
    private const int HSHELL_FLASH = 0x8006; // HSHELL_REDRAW | HSHELL_HIGHBIT
    private const int HSHELL_HIGHBIT = 0x8000;

    private static readonly uint WM_SHELLHOOKMESSAGE = User32.RegisterWindowMessage("SHELLHOOK");
    private static readonly IntPtr HInstance = Kernel32.GetModuleHandle(null);
    private const string ClassName = "MobreadDockAttentionWindow";
    private static readonly WndProc WndProcDelegate = WndProcImpl;   // kept alive for the class lifetime
    private static AttentionMonitor? _instance;

    static AttentionMonitor()
    {
        User32.RegisterClass(new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
            hInstance = HInstance,
            lpszClassName = ClassName,
        });
    }

    private readonly Action<string, IntPtr> _onFlash;
    private IntPtr _hwnd;

    public AttentionMonitor(Action<string, IntPtr> onFlash)
    {
        _onFlash = onFlash;
        _instance = this;
        // Shell hook messages aren't delivered to HWND_MESSAGE windows; use a plain hidden top-level.
        _hwnd = User32.CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, HInstance, IntPtr.Zero);
        if (_hwnd != IntPtr.Zero)
            User32.RegisterShellHookWindow(_hwnd);
    }

    private static readonly string OwnExe = Environment.ProcessPath ?? "";
    private static readonly Dictionary<string, (DateTime First, int Count)> _recent = new(StringComparer.OrdinalIgnoreCase);

    private static IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SHELLHOOKMESSAGE && _instance != null)
        {
            int code = (int)wParam & 0xFFFF;
            // Documented: HSHELL_FLASH = HSHELL_REDRAW | HSHELL_HIGHBIT. Observed on
            // Win11 24H2: FlashWindowEx arrives as bare HSHELL_REDRAW (6), one per
            // blink. A plain title change is also a single REDRAW, so require two
            // REDRAWs from the same app within 1.5 s — a real flash blinks at ~2 Hz.
            if ((code == HSHELL_FLASH || code == HSHELL_REDRAW) && lParam != User32.GetForegroundWindow())
            {
                string? path = GetProcessPath(lParam);
                if (path != null && !string.Equals(path, OwnExe, StringComparison.OrdinalIgnoreCase))
                {
                    bool fire = code == HSHELL_FLASH;
                    if (!fire)
                    {
                        var now = DateTime.UtcNow;
                        if (_recent.TryGetValue(path, out var r) && (now - r.First).TotalMilliseconds < 1500)
                        {
                            _recent[path] = (r.First, r.Count + 1);
                            fire = r.Count + 1 >= 2;
                        }
                        else _recent[path] = (now, 1);
                    }
                    if (fire)
                    {
                        _recent.Remove(path);
                        try { _instance._onFlash(path, lParam); } catch { }
                    }
                }
            }
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static string? GetProcessPath(IntPtr hWnd)
    {
        User32.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return null;
        IntPtr process = Kernel32.OpenProcess(Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buf = new StringBuilder(1024);
            uint size = (uint)buf.Capacity;
            return Kernel32.QueryFullProcessImageName(process, 0, buf, ref size) ? buf.ToString(0, (int)size) : null;
        }
        finally { Kernel32.CloseHandle(process); }
    }

    /// <summary>True if <paramref name="hwnd"/> (or its root owner) is the foreground window.</summary>
    public static bool IsForeground(IntPtr hwnd)
    {
        IntPtr fg = User32.GetForegroundWindow();
        return fg != IntPtr.Zero && (fg == hwnd || User32.GetAncestor(fg, 3 /* GA_ROOTOWNER */) == hwnd);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            User32.DeregisterShellHookWindow(_hwnd);
            User32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
