namespace CedroModernDock.Infrastructure.Windows.Adapters;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Adapter wrapping Win32WindowQuery to implement IWindowQueryGateway.
/// Direct port of JnaWindowQueryGateway.java.
/// </summary>
public class Win32WindowQueryGateway : IWindowQueryGateway
{
    public List<WindowInfo> FindOpenWindows(string? executablePath)
    {
        var windows = Win32WindowQuery.GetOpenWindows(executablePath);
        return windows.Select(w => new WindowInfo(w.Handle, w.Title)).ToList();
    }

    public List<RunningWindowInfo> FindTaskbarWindows()
    {
        return Win32WindowQuery.GetTaskbarWindows()
            .Select(w => new RunningWindowInfo(w.Handle, w.Title, w.ExecutablePath))
            .ToList();
    }

    public void Activate(WindowInfo windowInfo)
    {
        Win32WindowQuery.ActivateWindow(windowInfo.Handle);
    }

    public void Close(WindowInfo windowInfo)
    {
        Win32WindowQuery.CloseWindow(windowInfo.Handle);
    }

    public void Minimize(WindowInfo windowInfo)
    {
        if (windowInfo.Handle != IntPtr.Zero)
            User32.ShowWindow(windowInfo.Handle, Win32Constants.SW_MINIMIZE);
    }

    public bool IsForeground(WindowInfo windowInfo)
    {
        IntPtr fg = User32.GetForegroundWindow();
        if (fg == IntPtr.Zero || windowInfo.Handle == IntPtr.Zero) return false;
        // The foreground window may be an owned popup of the target; walk up.
        return fg == windowInfo.Handle || User32.GetAncestor(fg, GA_ROOTOWNER) == windowInfo.Handle;
    }

    public bool IsMinimized(WindowInfo windowInfo) =>
        windowInfo.Handle != IntPtr.Zero && User32.IsIconic(windowInfo.Handle);

    private const uint GA_ROOTOWNER = 3;
}
