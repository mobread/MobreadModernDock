namespace CedroModernDock.Core.Domain;

using CedroModernDock.Core.Application;

/// <summary>
/// Port for querying open native windows (for running-app indicators
/// and window-preview popups). The WindowInfo record is shared so the
/// application layer never depends on Win32 HWND types directly.
/// </summary>
public interface IWindowQueryGateway
{
    List<WindowInfo> FindOpenWindows(string? executablePath);
    List<RunningWindowInfo> FindTaskbarWindows();
    void Activate(WindowInfo windowInfo);
    void Close(WindowInfo windowInfo);
    void Minimize(WindowInfo windowInfo) { }
    bool IsForeground(WindowInfo windowInfo) => false;
    bool IsMinimized(WindowInfo windowInfo) => false;
}

/// <summary>Minimal info required to activate and label a window.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title);

/// <summary>Taskbar-visible window plus its owning executable path.</summary>
public sealed record RunningWindowInfo(IntPtr Handle, string Title, string ExecutablePath);
