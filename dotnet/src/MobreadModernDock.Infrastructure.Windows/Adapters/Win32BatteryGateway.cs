namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Runtime.InteropServices;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;

/// <summary>Battery state via <c>GetSystemPowerStatus</c> (kernel32).</summary>
public sealed class Win32BatteryGateway : IBatteryGateway
{
    public BatteryStatus Read()
    {
        if (!GetSystemPowerStatus(out var s))
            return new BatteryStatus(false, 0, false, false, null);

        // BatteryFlag 128 = no system battery; 255 = unknown.
        bool hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        bool pluggedIn = s.ACLineStatus == 1;
        bool charging = (s.BatteryFlag & 8) != 0;
        int percent = BatteryFormats.ClampPercent(s.BatteryLifePercent);
        TimeSpan? remaining = s.BatteryLifeTime >= 0 && !pluggedIn
            ? TimeSpan.FromSeconds(s.BatteryLifeTime)
            : null;
        return new BatteryStatus(hasBattery, percent, charging, pluggedIn, remaining);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
