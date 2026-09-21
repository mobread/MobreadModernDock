namespace MobreadModernDock.Infrastructure.Windows.Native;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Blur / acrylic backdrop behind a borderless window, via the undocumented
/// <c>SetWindowCompositionAttribute</c> accent policy (the same mechanism the
/// Win10/11 taskbar and start menu use).
///
/// Two visual modes are offered:
/// <list type="bullet">
/// <item><b>Blur</b> (ACCENT_ENABLE_BLURBEHIND): a plain gaussian blur of
/// whatever is behind the window. Cheap and smooth while dragging.</item>
/// <item><b>Acrylic</b> (ACCENT_ENABLE_ACRYLICBLURBEHIND): the Fluent acrylic
/// material — blur plus a tint and noise layer. Richer, but DWM re-renders it
/// on every move, which makes dragging noticeably heavier.</item>
/// </list>
///
/// The accent tint is supplied as a premultiplied-looking ABGR value; the
/// window's own content is still drawn on top, so the dock keeps painting its
/// rounded <c>Border</c> and only leaves the background to the backdrop.
/// </summary>
public static class WindowBlur
{
    public const string ModeNone = "none";
    public const string ModeBlur = "blur";
    public const string ModeAcrylic = "acrylic";

    private enum AccentState
    {
        Disabled = 0,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Applies (or removes) the backdrop. <paramref name="tintOpacity"/> is
    /// 0..1 and only affects the acrylic tint — the plain blur mode ignores the
    /// tint entirely so the dock's own background colour stays in charge.
    /// </summary>
    public static bool Apply(IntPtr hwnd, string mode, byte r, byte g, byte b, double tintOpacity)
    {
        if (hwnd == IntPtr.Zero) return false;

        AccentState state = mode switch
        {
            ModeBlur => AccentState.EnableBlurBehind,
            ModeAcrylic => AccentState.EnableAcrylicBlurBehind,
            _ => AccentState.Disabled,
        };

        // ACCENT_ENABLE_ACRYLICBLURBEHIND with a fully transparent gradient
        // colour renders nothing at all, so the acrylic tint gets at least a
        // faint alpha. The plain blur mode is driven purely by the window's
        // own painted background.
        byte alpha = state == AccentState.EnableAcrylicBlurBehind
            ? (byte)Math.Clamp(tintOpacity * 255.0, 16, 255)
            : (byte)0;
        uint gradient = ((uint)alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

        var policy = new AccentPolicy
        {
            AccentState = (int)state,
            // 2 = draw the accent on every edge. Without it the backdrop is
            // inset by the (nonexistent) border of a decorationless window.
            AccentFlags = 2,
            GradientColor = gradient,
            AnimationId = 0,
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false; // pre-Win10: no accent policy at all
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

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
