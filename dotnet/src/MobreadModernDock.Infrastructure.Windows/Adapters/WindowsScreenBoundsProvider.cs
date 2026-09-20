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
}
