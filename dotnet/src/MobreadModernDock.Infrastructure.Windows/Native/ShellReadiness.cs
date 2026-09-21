namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Explorer creates Progman, then Shell_TrayWnd, then (lazily) the WorkerW
/// that hosts the desktop icons. A dock auto-started from the Run key can
/// beat all three. Poll until the taskbar exists — by then Progman is up
/// too — and give the shell one extra beat to settle.
/// </summary>
public static class ShellReadiness
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    public static bool IsShellReady() =>
        FindWindowW("Progman", null) != IntPtr.Zero && FindWindowW("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>Returns true if the shell was ready before the timeout.</summary>
    public static bool WaitForShell(TimeSpan timeout)
    {
        if (IsShellReady()) return true;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            Thread.Sleep(250);
            if (IsShellReady())
            {
                // Progman answers WM_PROGMAN_CREATE_DESKTOP immediately but the
                // icon WorkerW appears a moment later; without this pause the
                // dock parents to bare Progman and sits under the icons.
                Thread.Sleep(1500);
                return true;
            }
        }
        return false;
    }
}
