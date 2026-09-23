namespace MobreadModernDock.Views;

using System;
using System.Collections.Generic;
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
    /// Closes the open menu unless the click landed inside it or inside one
    /// of its open submenus. Each popup is its own top-level window, so the
    /// screen rects come from the PopupRoots rather than from the anchor.
    /// </summary>
    private void OnGlobalClick(int screenX, int screenY)
    {
        if (_open is not { IsOpen: true } menu)
        {
            Dispose();
            return;
        }

        var point = new PixelPoint(screenX, screenY);
        if (menu.GetVisualRoot() is Visual root && ScreenRect(root).Contains(point))
            return; // inside the menu: let Avalonia handle the selection itself

        // Submenus open their own popups; a click on one of those must not be
        // treated as a click outside (it would close the whole menu before
        // the item's Click could fire).
        foreach (var submenu in OpenSubmenus(menu))
        {
            if (submenu.GetVisualRoot() is Visual subRoot && ScreenRect(subRoot).Contains(point))
                return;
        }

        menu.Close();
        Dispose();
    }

    /// <summary>
    /// A popup root's screen rectangle in physical pixels. <c>PointToScreen</c>
    /// already returns physical coordinates, but <c>Bounds</c> is in DIPs and
    /// must be scaled - at 125% an unscaled rect misses the right/bottom 20%
    /// of the menu, so clicks there read as "outside" and dismiss it.
    /// </summary>
    private static PixelRect ScreenRect(Visual root)
    {
        var topLeft = root.PointToScreen(new Point(0, 0));
        var size = root.Bounds.Size;
        double scale = (root as TopLevel)?.RenderScaling ?? 1.0;
        return new PixelRect(topLeft,
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)));
    }

    /// <summary>The presenters of every submenu currently open under the menu, at any depth.</summary>
    private static IEnumerable<Control> OpenSubmenus(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (item is not MenuItem { IsSubMenuOpen: true } menuItem) continue;
            if (menuItem.Presenter is Control presenter && presenter.GetVisualRoot() != parent.GetVisualRoot())
                yield return presenter;
            foreach (var nested in OpenSubmenus(menuItem))
                yield return nested;
        }
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
        _open = null;
    }
}
