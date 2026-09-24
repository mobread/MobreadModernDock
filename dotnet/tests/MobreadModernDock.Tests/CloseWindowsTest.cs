namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Right-click → "Close window(s)" on a dock icon. WM_CLOSE goes only to the
/// windows the taskbar would list: the per-exe match also returns titled
/// shell surfaces (explorer.exe owns the taskbars) and owned dialogs, and
/// posting WM_CLOSE to those would break the desktop.
/// </summary>
public class CloseWindowsTest
{
    private sealed class FakeGateway : IWindowQueryGateway
    {
        public List<WindowInfo> Open = new();
        public List<RunningWindowInfo> Taskbar = new();
        public List<IntPtr> Closed = new();
        public List<WindowInfo> FindOpenWindows(string? executablePath) => Open;
        public List<RunningWindowInfo> FindTaskbarWindows() => Taskbar;
        public void Activate(WindowInfo windowInfo) { }
        public void Close(WindowInfo windowInfo) => Closed.Add(windowInfo.Handle);
    }

    private const string Exe = @"C:\Windows\explorer.exe";

    [Fact]
    public void ClosesOnlyTaskbarWindows()
    {
        var gw = new FakeGateway();
        gw.Open.Add(new WindowInfo(1, "Documents"));
        gw.Open.Add(new WindowInfo(2, "Downloads"));
        gw.Open.Add(new WindowInfo(99, "Shell surface")); // not a taskbar window
        gw.Taskbar.Add(new RunningWindowInfo(1, "Documents", Exe));
        gw.Taskbar.Add(new RunningWindowInfo(2, "Downloads", Exe));
        var svc = new WindowPreviewService(gw);

        Assert.Equal(2, svc.CountOpenWindows(Exe));
        Assert.Equal(2, svc.CloseAll(Exe));
        Assert.Equal(new IntPtr[] { 1, 2 }, gw.Closed);
    }

    [Fact]
    public void NothingOpenClosesNothing()
    {
        var gw = new FakeGateway();
        var svc = new WindowPreviewService(gw);
        Assert.Equal(0, svc.CountOpenWindows(Exe));
        Assert.Equal(0, svc.CloseAll(Exe));
        Assert.Empty(gw.Closed);
    }

    [Fact]
    public void DuplicateHandlesAreClosedOnce()
    {
        // UWP: two CoreWindow matches can resolve to the same frame hwnd.
        var gw = new FakeGateway();
        gw.Open.Add(new WindowInfo(5, "Settings"));
        gw.Open.Add(new WindowInfo(5, "Settings"));
        gw.Taskbar.Add(new RunningWindowInfo(5, "Settings", Exe));
        var svc = new WindowPreviewService(gw);
        Assert.Equal(1, svc.CloseAll(Exe));
        Assert.Single(gw.Closed);
    }
}
