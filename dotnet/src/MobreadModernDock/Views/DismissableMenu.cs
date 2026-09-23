namespace MobreadModernDock.Views;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Opens a context menu from a <c>WS_EX_NOACTIVATE</c> window and arranges
/// for it to be dismissed when the user clicks anywhere outside it.
///
/// Such windows (the dock, the widgets) are never activated and never
/// deactivated, so Avalonia's usual light-dismiss never fires and the menu
/// would sit there after a click on the desktop or another app. A low-level
/// mouse hook supplies the clicks the window cannot see; it only lives while
/// a menu is open. One instance per owning window.
/// </summary>
public sealed class DismissableMenu : IDisposable
{
    private GlobalMouseHook? _hook;
    private ContextMenu? _open;

    public void Open(ContextMenu menu, Control anchor)
    {
        Dispose();

        menu.Closed += (_, _) => Dispose();
        menu.Open(anchor);

        // Arm after the popup exists, so its bounds can be hit-tested.
        Dispatcher.UIThread.Post(() =>
        {
            if (!menu.IsOpen) return;
            _open = menu;
            var hook = new GlobalMouseHook((x, y) =>
                Dispatcher.UIThread.Post(() => OnGlobalClick(x, y)));
            if (hook.IsInstalled) _hook = hook;
            else hook.Dispose(); // no hook: the menu still closes on selection
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Closes the open menu unless the click landed inside it. The popup is
    /// its own top-level window, so its screen rect comes from the PopupRoot
    /// rather than from the anchor control.
    /// </summary>
    private void OnGlobalClick(int screenX, int screenY)
    {
        if (_open is not { IsOpen: true } menu)
        {
            Dispose();
            return;
        }

        if (menu.GetVisualRoot() is Visual root)
        {
            var topLeft = root.PointToScreen(new Point(0, 0));
            var size = root.Bounds.Size;
            var rect = new PixelRect(topLeft,
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
            // Inside the menu: let Avalonia handle the selection itself.
            if (rect.Contains(new PixelPoint(screenX, screenY))) return;
        }

        menu.Close();
        Dispose();
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
        _open = null;
    }
}
