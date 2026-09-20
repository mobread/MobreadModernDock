namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Detects whether a fullscreen application (game, video, presentation)
/// currently owns the screen, so the dock and widgets can get out of the way.
///
/// Two signals are combined:
/// <list type="bullet">
/// <item><c>SHQueryUserNotificationState</c> — the shell's own "don't disturb"
/// state: <c>RUNNING_D3D_FULL_SCREEN</c> for exclusive fullscreen,
/// <c>BUSY</c>/<c>PRESENTATION_MODE</c> for borderless fullscreen and
/// presentations. This is what suppresses toast notifications in games.</item>
/// <item>Foreground-window geometry — a non-shell foreground window whose
/// rect covers its entire monitor. Catches borderless games that don't flip
/// the notification state.</item>
/// </list>
/// </summary>
public static class FullscreenDetector
{
    public static bool IsFullscreenAppActive()
    {
        if (SHQueryUserNotificationState(out int state) == 0)
        {
            if (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_BUSY || state == QUNS_PRESENTATION_MODE)
                return true;
        }
        return ForegroundCoversMonitor();
    }

    private static bool ForegroundCoversMonitor()
    {
        IntPtr fg = User32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        // Ignore the desktop, the shell and ourselves.
        var cls = new StringBuilder(64);
        User32.GetClassName(fg, cls, cls.Capacity);
        string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            || c.StartsWith("Avalonia-", StringComparison.Ordinal))
            return false;
        User32.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;

        if (!User32.GetWindowRect(fg, out RECT wr)) return false;
        IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;

        // Covers the full monitor (not just the work area) => fullscreen.
        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top
            && wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
    }

    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
