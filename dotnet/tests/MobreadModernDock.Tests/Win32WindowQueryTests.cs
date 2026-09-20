using MobreadModernDock.Infrastructure.Windows.Native;

namespace MobreadModernDock.Tests;

/// <summary>
/// Phase 0 spike tests: verify the Win32 window-enumeration port (the direct
/// replacement for NativeWindowUtils) works correctly under .NET.
/// </summary>
public class Win32WindowQueryTests
{
    [Fact]
    public void GetOpenWindows_WithNullPath_ReturnsEmptyList()
    {
        var windows = Win32WindowQuery.GetOpenWindows(null);
        Assert.Empty(windows);
    }

    [Fact]
    public void GetOpenWindows_WithEmptyPath_ReturnsEmptyList()
    {
        var windows = Win32WindowQuery.GetOpenWindows(string.Empty);
        Assert.Empty(windows);
    }

    [Fact]
    public void GetOpenWindows_WithNonexistentExe_ReturnsEmptyList()
    {
        var windows = Win32WindowQuery.GetOpenWindows(@"C:\This\Does\Not\Exist\fake.exe");
        Assert.Empty(windows);
    }

    [Fact]
    public void GetOpenWindows_WithExplorerPath_ReturnsNonNullList()
    {
        // explorer.exe is virtually always running on Windows.
        string explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");

        var windows = Win32WindowQuery.GetOpenWindows(explorerPath);

        Assert.NotNull(windows);
        // Don't assert non-empty — explorer might have no visible top-level
        // windows in a headless test session, but the call must not crash.
    }

    [Fact]
    public void WindowInfo_Record_HoldsHandleAndTitle()
    {
        var info = new Win32WindowQuery.WindowInfo(new IntPtr(0x1234), "Test Window");
        Assert.Equal(new IntPtr(0x1234), info.Handle);
        Assert.Equal("Test Window", info.Title);
    }
}