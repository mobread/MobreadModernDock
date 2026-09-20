using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using MobreadModernDock.Infrastructure.Windows.Native;

namespace MobreadModernDock.Views;

/// <summary>
/// Screen-coordinate helpers that stay correct when the dock is reparented
/// to the desktop. Avalonia's <c>PointToScreen</c> and <c>Screens</c> report
/// coordinates relative to the native parent (Progman spans the whole
/// virtual desktop), so on multi-monitor layouts whose primary is not at the
/// virtual origin every popup anchored with them lands one screen away.
/// These helpers read true rects from Win32 instead.
/// </summary>
internal static class ScreenGeometry
{
    /// <summary>Absolute screen rectangle (physical pixels) of a control inside a top-level window.</summary>
    public static PixelRect ControlScreenRect(Control control)
    {
        var root = TopLevel.GetTopLevel(control) as Window;
        if (root == null) return default;

        var handle = root.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !User32.GetWindowRect(handle, out RECT wr))
        {
            var p = control.PointToScreen(new Point(0, 0));
            double s = root.RenderScaling;
            return new PixelRect(p.X, p.Y, (int)(control.Bounds.Width * s), (int)(control.Bounds.Height * s));
        }

        double scale = root.RenderScaling;
        var tl = control.TranslatePoint(new Point(0, 0), root) ?? new Point(0, 0);
        return new PixelRect(
            wr.Left + (int)Math.Round(tl.X * scale),
            wr.Top + (int)Math.Round(tl.Y * scale),
            (int)Math.Round(control.Bounds.Width * scale),
            (int)Math.Round(control.Bounds.Height * scale));
    }

    /// <summary>Absolute screen rectangle (physical pixels) of a window.</summary>
    public static PixelRect WindowScreenRect(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero && User32.GetWindowRect(handle, out RECT wr))
            return new PixelRect(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
        return new PixelRect(window.Position, PixelSize.FromSize(window.Bounds.Size, window.RenderScaling));
    }

    /// <summary>Work area (physical pixels) of the monitor containing a point, via Win32.</summary>
    public static PixelRect WorkAreaAt(PixelPoint point)
    {
        IntPtr mon = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            return new PixelRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
        return new PixelRect(0, 0, 1920, 1080);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
