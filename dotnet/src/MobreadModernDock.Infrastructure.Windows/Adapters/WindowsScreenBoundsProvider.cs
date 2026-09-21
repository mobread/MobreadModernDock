namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using MobreadModernDock.Core.Application;

/// <summary>
/// Windows-specific implementation of IScreenBoundsProvider.
/// Uses the primary screen's working area (bounds excluding taskbar)
/// to replicate JavaFX Screen.getPrimary().getVisualBounds().
/// </summary>
public class WindowsScreenBoundsProvider : IScreenBoundsProvider
{
    public ScreenBounds GetPrimaryScreenBounds()
    {
        // System.Windows.Forms.Screen.PrimaryScreen.WorkingArea gives the
        // taskbar-excluded bounds, equivalent to JavaFX visual bounds.
        var workingArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        return new ScreenBounds(
            workingArea.X, workingArea.Y,
            workingArea.Width, workingArea.Height);
    }

    /// <summary>#10 Every monitor, keyed by its GDI device name (\\.\DISPLAY1 …), which is stable across sessions.</summary>
    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var list = new List<ScreenInfo>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
        {
            var w = s.WorkingArea;
            list.Add(new ScreenInfo(s.DeviceName, s.Primary, new ScreenBounds(w.X, w.Y, w.Width, w.Height)));
        }
        if (list.Count == 0) list.Add(new ScreenInfo("primary", true, GetPrimaryScreenBounds()));
        return list;
    }
}
