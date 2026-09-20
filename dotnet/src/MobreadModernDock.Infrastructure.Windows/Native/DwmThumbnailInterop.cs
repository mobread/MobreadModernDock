using System;
using System.Runtime.InteropServices;

namespace MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Minimal DWM thumbnail interop: renders a live preview of a source window
/// (HWND) into a destination window's client area. All calls are best-effort:
/// failures return false and must never crash the caller.
/// </summary>
public static class DwmThumbnailInterop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DwmSize
    {
        public int Cx;
        public int Cy;
    }

    // Native layout: dwFlags(4) rcDestination(16) rcSource(16) opacity(1)
    // padding(3) fVisible(4) fSourceClientAreaOnly(4) = 48 bytes. Field order
    // and the rcSource placeholder MUST be kept exactly as-is.
    [StructLayout(LayoutKind.Sequential)]
    public struct DwmThumbnailProperties
    {
        public uint Flags;
        public RECT Destination;
        public RECT Source;       // zero = whole source window
        public byte Opacity;
        public int Visible;       // BOOL
        public int SourceClientAreaOnly;
    }

    public const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    public const uint DWM_TNP_OPACITY = 0x00000004;
    public const uint DWM_TNP_VISIBLE = 0x00000008;

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DwmThumbnailProperties ptnProperties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnailId, out DwmSize pSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>DWMWA_CLOAKED: 0=not cloaked, 1=cloaked by app, 2=cloaked by shell (Win+D).</summary>
    private const int DWMWA_CLOAKED = 14;

    /// <summary>True when the window is cloaked (hidden by the shell, e.g. Win+D).</summary>
    public static bool IsCloaked(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ref cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Registers a live thumbnail of <paramref name="sourceHwnd"/> into <paramref name="destHwnd"/>.</summary>
    public static bool Register(IntPtr destHwnd, IntPtr sourceHwnd, out IntPtr thumbId)
    {
        thumbId = IntPtr.Zero;
        if (destHwnd == IntPtr.Zero || sourceHwnd == IntPtr.Zero) return false;
        return DwmRegisterThumbnail(destHwnd, sourceHwnd, out thumbId) == 0;
    }

    public static bool Unregister(IntPtr thumbId)
    {
        if (thumbId == IntPtr.Zero) return false;
        return DwmUnregisterThumbnail(thumbId) == 0;
    }

    /// <summary>
    /// Positions the thumbnail at <paramref name="dest"/> (physical pixels of the
    /// destination window client area), optionally letterboxing via a pre-computed rect.
    /// </summary>
    public static bool Update(IntPtr thumbId, RECT dest, int opacity = 255, bool visible = true)
    {
        if (thumbId == IntPtr.Zero) return false;
        var props = new DwmThumbnailProperties
        {
            Flags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
            Destination = dest,
            Source = default,
            Opacity = (byte)opacity,
            Visible = visible ? 1 : 0,
            SourceClientAreaOnly = 0
        };
        return DwmUpdateThumbnailProperties(thumbId, ref props) == 0;
    }

    public static bool QuerySourceSize(IntPtr thumbId, out DwmSize size)
    {
        size = default;
        if (thumbId == IntPtr.Zero) return false;
        return DwmQueryThumbnailSourceSize(thumbId, out size) == 0;
    }

    /// <summary>
    /// Sets a DWM window attribute (e.g. DWMWA_WINDOW_CORNER_PREFERENCE).
    /// Best-effort: returns false on failure (e.g. unsupported on Win10).
    /// </summary>
    public static bool SetWindowAttribute(IntPtr hwnd, int attribute, int value)
    {
        if (hwnd == IntPtr.Zero) return false;
        int v = value;
        return DwmSetWindowAttribute(hwnd, attribute, ref v, sizeof(int)) == 0;
    }

    /// <summary>Converts an ARGB color to a COLORREF (0x00BBGGRR) for DWMWA_BORDER_COLOR.</summary>
    public static uint ToColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
}
