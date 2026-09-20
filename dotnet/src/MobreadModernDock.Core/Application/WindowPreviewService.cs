namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>Direct port of WindowPreviewService.</summary>
public class WindowPreviewService
{
    private readonly IWindowQueryGateway _windowQueryGateway;

    public WindowPreviewService(IWindowQueryGateway windowQueryGateway)
    {
        _windowQueryGateway = windowQueryGateway;
    }

    public List<WindowInfo> LoadPreview(DockProgramItemModel item) =>
        _windowQueryGateway.FindOpenWindows(item.ExecutablePath);

    public bool HasOpenWindows(string? executablePath) =>
        _windowQueryGateway.FindOpenWindows(executablePath).Count > 0;

    public List<RunningWindowInfo> FindTaskbarWindows() =>
        _windowQueryGateway.FindTaskbarWindows();

    public void Activate(WindowInfo windowInfo) => _windowQueryGateway.Activate(windowInfo);

    public void Close(WindowInfo windowInfo) => _windowQueryGateway.Close(windowInfo);

    public void Minimize(WindowInfo windowInfo) => _windowQueryGateway.Minimize(windowInfo);

    public bool IsForeground(WindowInfo windowInfo) => _windowQueryGateway.IsForeground(windowInfo);

    public bool IsMinimized(WindowInfo windowInfo) => _windowQueryGateway.IsMinimized(windowInfo);

    /// <summary>
    /// Taskbar-style click on a running app:
    /// no windows -> launch (caller handles), one window -> toggle
    /// focus/minimize, several -> focus the first non-foreground one (so
    /// repeated clicks cycle). Returns false when nothing was open.
    /// </summary>
    public bool ClickRunning(DockProgramItemModel item)
    {
        var windows = LoadPreview(item);
        if (windows.Count == 0) return false;

        if (windows.Count == 1)
        {
            var w = windows[0];
            if (IsForeground(w) && !IsMinimized(w)) Minimize(w);
            else Activate(w);
            return true;
        }

        int fgIndex = windows.FindIndex(IsForeground);
        var next = windows[(fgIndex + 1) % windows.Count];
        Activate(next);
        return true;
    }

    /// <summary>Scroll over an icon: step through the app's windows in either direction.</summary>
    public void CycleWindows(DockProgramItemModel item, int direction)
    {
        var windows = LoadPreview(item);
        if (windows.Count == 0) return;
        int fgIndex = windows.FindIndex(IsForeground);
        int next = fgIndex < 0 ? 0 : ((fgIndex + Math.Sign(direction)) % windows.Count + windows.Count) % windows.Count;
        Activate(windows[next]);
    }
}
