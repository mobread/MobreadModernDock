namespace MobreadModernDock.Infrastructure.Windows.Native;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Window-region helper for the dock's backdrop, plus the vocabulary of
/// backdrop mode names persisted in config.
///
/// <para><b>Why there is no accent-policy call here any more.</b> The blur used
/// to be applied with the undocumented <c>SetWindowCompositionAttribute</c>
/// accent policy (the mechanism the Win10/11 taskbar uses). That cannot work
/// for this app: Avalonia creates its windows with <c>WS_EX_NOREDIRECTIONBITMAP</c>
/// and composes them through DirectComposition, while the accent policy paints
/// into a window's <i>redirection surface</i>. With no redirection surface the
/// call returns success and draws nothing — verified on Win11 25H2 (26200) by
/// capturing the composed frame with Desktop Duplication against a
/// saturated-colour backdrop: the pixels behind the bar were identical with the
/// accent enabled and disabled, and none of the accent states (3 blur, 4
/// acrylic, 5 host-backdrop) or flag combinations changed that.</para>
///
/// <para>Note that GDI capture (BitBlt / PIL ImageGrab) cannot be used to judge
/// this either way — it excludes DWM backdrop compositing and shows black. Use
/// Desktop Duplication.</para>
///
/// <para>The backdrop is therefore driven by Avalonia's own WinUI-Composition
/// implementation via <c>Window.TransparencyLevelHint</c> (see
/// <c>MainWindow.ApplyBackdrop</c>). The window region below is still required:
/// the backdrop fills the whole <i>window</i>, so without a region it shows as
/// a square slab covering the rounded corners and the transparent bounce
/// headroom.</para>
/// </summary>
public static class WindowBlur
{
    public const string ModeNone = "none";
    public const string ModeBlur = "blur";
    public const string ModeAcrylic = "acrylic";

    /// <summary>True when the supplied mode asks for any backdrop at all.</summary>
    public static bool IsEnabled(string? mode) => mode is ModeBlur or ModeAcrylic;

    // --- Window region: confines the backdrop to the visible bar ---

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    /// <summary>
    /// Clips the window to a rounded rectangle inset from its own bounds.
    /// The accent backdrop fills the whole <i>window</i>, not just the painted
    /// <c>Border</c>: without a region the blur would show as a square slab
    /// covering the transparent bounce headroom and the rounded corners. The
    /// inset/radius are given in the window's own layout units and scaled to
    /// physical pixels by the caller-supplied <paramref name="scale"/>.
    ///
    /// Pass a zero-size rect to drop the region again (blur turned off).
    /// </summary>
    public static void SetRoundedRegion(IntPtr hwnd, double left, double top,
        double width, double height, double radius, double scale)
    {
        if (hwnd == IntPtr.Zero) return;
        if (width <= 0 || height <= 0)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        int x1 = (int)Math.Round(left * scale);
        int y1 = (int)Math.Round(top * scale);
        // CreateRoundRectRgn's bottom/right are exclusive, hence the +1.
        int x2 = (int)Math.Round((left + width) * scale) + 1;
        int y2 = (int)Math.Round((top + height) * scale) + 1;
        // The API takes ellipse *diameters*, not the CSS-style corner radius.
        int d = (int)Math.Round(radius * scale) * 2;

        IntPtr region = CreateRoundRectRgn(x1, y1, x2, y2, d, d);
        if (region == IntPtr.Zero) return;
        // SetWindowRgn takes ownership on success; only free it if it failed.
        if (SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region);
    }

    /// <summary>Removes any window region previously set (back to the full rect).</summary>
    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowRgn(hwnd, IntPtr.Zero, true);
    }
}
