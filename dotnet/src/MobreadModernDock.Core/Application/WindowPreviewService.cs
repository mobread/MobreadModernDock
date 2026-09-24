namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>Lists the open windows of a program, for the hover preview popup.</summary>
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

    /// <summary>
    /// Taskbar "Close window" / "Close all windows": asks every open window of
    /// the program to close (WM_CLOSE, so apps can still prompt to save).
    /// Returns how many windows were asked.
    /// </summary>
    public int CloseAll(string? executablePath)
    {
        var windows = ClosableWindows(executablePath);
        foreach (var w in windows) _windowQueryGateway.Close(w);
        return windows.Count;
    }

    /// <summary>Number of windows "Close window(s)" would close (drives the menu caption).</summary>
    public int CountOpenWindows(string? executablePath) => ClosableWindows(executablePath).Count;

    /// <summary>
    /// The program's windows that the taskbar itself would list. The plain
    /// per-exe match also picks up titled shell surfaces (explorer.exe owns
    /// the taskbars and desktop helpers) and owned dialogs; WM_CLOSE must
    /// never reach those, so only windows that are also taskbar windows count.
    /// </summary>
    private List<WindowInfo> ClosableWindows(string? executablePath)
    {
        var taskbar = _windowQueryGateway.FindTaskbarWindows().Select(w => w.Handle).ToHashSet();
        return _windowQueryGateway.FindOpenWindows(executablePath)
            .Where(w => taskbar.Contains(w.Handle))
            .DistinctBy(w => w.Handle)
            .ToList();
    }

    public void Minimize(WindowInfo windowInfo) => _windowQueryGateway.Minimize(windowInfo);

    public bool IsForeground(WindowInfo windowInfo) => _windowQueryGateway.IsForeground(windowInfo);

    public bool IsMinimized(WindowInfo windowInfo) => _windowQueryGateway.IsMinimized(windowInfo);

    public string? ForegroundExecutablePath() => _windowQueryGateway.ForegroundExecutablePath();

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
