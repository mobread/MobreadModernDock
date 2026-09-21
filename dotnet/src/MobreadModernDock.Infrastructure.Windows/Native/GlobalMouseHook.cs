namespace MobreadModernDock.Infrastructure.Windows.Native;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Reports mouse button presses anywhere on the desktop, including in other
/// applications, via a low-level mouse hook.
///
/// The dock exists to notice clicks it would otherwise never see: its window
/// is <c>WS_EX_NOACTIVATE</c>, so it is never activated and never deactivated,
/// which is what normally light-dismisses a popup. Without this, a context
/// menu stays on screen after the user clicks the desktop or another app.
///
/// The hook is called on the thread that installed it, so install it from the
/// UI thread (which pumps messages) and keep the callback cheap — everything
/// in the system routes through it while it lives.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // Kept in a field so the GC cannot collect the callback while the native
    // hook still points at it (the same trap as the window subclass proc).
    private readonly HookProc _proc;
    private readonly Action<int, int> _onButtonDown;
    private IntPtr _hook;

    /// <param name="onButtonDown">Called with the screen coordinates of the press.</param>
    public GlobalMouseHook(Action<int, int> onButtonDown)
    {
        _onButtonDown = onButtonDown;
        _proc = Callback;
        // A null module handle is correct for WH_MOUSE_LL and avoids pinning
        // a module that may be unloaded.
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    /// <summary>False when the hook could not be installed; the caller should fall back.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_NCLBUTTONDOWN)
            {
                try
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _onButtonDown(data.X, data.Y);
                }
                catch { /* a throw here would break mouse input system-wide */ }
            }
        }
        // Always pass the click on. Swallowing it would mean a user clicking
        // another window has to click twice, and a bug in the dismiss logic
        // would silently eat input everywhere.
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
