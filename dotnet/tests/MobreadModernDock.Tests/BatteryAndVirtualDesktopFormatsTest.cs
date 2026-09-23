namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;

public class BatteryAndVirtualDesktopFormatsTest
{
    private static string Loc(string key) => key switch
    {
        "widget.battery.charging" => "Charging",
        "widget.battery.pluggedIn" => "Plugged in",
        "widget.battery.remaining" => "{0} left",
        _ => key,
    };

    [Theory]
    [InlineData(0, null)]
    [InlineData(45 * 60, "45m")]
    [InlineData(83 * 60, "1h 23m")]
    [InlineData(3 * 3600 + 5 * 60 + 40, "3h 05m")]
    public void FormatsRemainingTime(int seconds, string? expected)
    {
        Assert.Equal(expected, BatteryFormats.FormatRemaining(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void NullRemainingIsNull() => Assert.Null(BatteryFormats.FormatRemaining(null));

    [Theory]
    [InlineData(255, 0)]   // API's "unknown"
    [InlineData(-1, 0)]
    [InlineData(57, 57)]
    [InlineData(100, 100)]
    public void ClampsPercent(int raw, int expected) => Assert.Equal(expected, BatteryFormats.ClampPercent(raw));

    [Fact]
    public void StatusLinePrefersChargingThenPluggedThenTime()
    {
        var charging = new BatteryStatus(true, 50, true, true, null);
        Assert.Equal("Charging", BatteryFormats.StatusLine(charging, true, Loc));

        var plugged = new BatteryStatus(true, 100, false, true, null);
        Assert.Equal("Plugged in", BatteryFormats.StatusLine(plugged, true, Loc));

        var draining = new BatteryStatus(true, 60, false, false, TimeSpan.FromMinutes(95));
        Assert.Equal("1h 35m left", BatteryFormats.StatusLine(draining, true, Loc));
        Assert.Equal("", BatteryFormats.StatusLine(draining, false, Loc));

        var none = new BatteryStatus(false, 0, false, true, null);
        Assert.Equal("", BatteryFormats.StatusLine(none, true, Loc));
    }

    [Fact]
    public void UnpacksGuidsFromRegistryBlob()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var packed = a.ToByteArray().Concat(b.ToByteArray()).Concat(new byte[] { 1, 2, 3 }).ToArray(); // trailing junk ignored
        Assert.Equal(new[] { a, b }, VirtualDesktopFormats.UnpackIds(packed));
        Assert.Empty(VirtualDesktopFormats.UnpackIds(null));
        Assert.Empty(VirtualDesktopFormats.UnpackIds(new byte[7]));
    }

    [Fact]
    public void LabelFallsBackToNumber()
    {
        var named = new VirtualDesktopInfo(Guid.NewGuid(), "Work");
        var unnamed = new VirtualDesktopInfo(Guid.NewGuid(), "");
        Assert.Equal("Work", VirtualDesktopFormats.Label(named, 0, showNames: true));
        Assert.Equal("1", VirtualDesktopFormats.Label(named, 0, showNames: false));
        Assert.Equal("3", VirtualDesktopFormats.Label(unnamed, 2, showNames: true));
    }

    [Theory]
    [InlineData(0, 2, 4, 2)]
    [InlineData(3, 1, 4, -2)]
    [InlineData(1, 1, 4, 0)]
    [InlineData(-1, 1, 4, 0)]  // unknown current
    [InlineData(0, 4, 4, 0)]   // target out of range
    public void StepsBetweenDesktops(int current, int target, int count, int expected)
    {
        Assert.Equal(expected, VirtualDesktopFormats.StepsBetween(current, target, count));
    }
}
