using MobreadModernDock.Core.Application;
using Xunit;

namespace MobreadModernDock.Tests;

public class ClockFormatsTest
{
    [Fact]
    public void Resolve_KnownPreset_ReturnsItsFormats()
    {
        var (t, d) = ClockFormats.Resolve("time24-date", null);
        Assert.Equal("HH:mm", t);
        Assert.Equal("ddd, d MMM", d);
    }

    [Fact]
    public void Resolve_UnknownPreset_FallsBackToFirst()
    {
        var (t, d) = ClockFormats.Resolve("nope", null);
        Assert.Equal(ClockFormats.Presets[0].TimeFormat, t);
        Assert.Equal(ClockFormats.Presets[0].DateFormat, d);
    }

    [Fact]
    public void Resolve_Custom_UsesUserFormat_NoDateLine()
    {
        var (t, d) = ClockFormats.Resolve(ClockFormats.Custom, "yyyy-MM-dd HH:mm");
        Assert.Equal("yyyy-MM-dd HH:mm", t);
        Assert.Null(d);
    }

    [Fact]
    public void Resolve_CustomEmpty_FallsBackToTime()
    {
        var (t, _) = ClockFormats.Resolve(ClockFormats.Custom, "   ");
        Assert.Equal("HH:mm", t);
    }

    [Fact]
    public void Format_InvalidPattern_DoesNotThrow()
    {
        var now = new DateTime(2026, 9, 20, 14, 5, 9);
        // An unterminated quote is a FormatException in ToString(format).
        string s = ClockFormats.Format(now, "HH:mm 'oops");
        Assert.Equal("14:05", s);
    }

    [Fact]
    public void Format_AppliesPattern()
    {
        var now = new DateTime(2026, 9, 20, 14, 5, 9);
        Assert.Equal("14:05:09", ClockFormats.Format(now, "HH:mm:ss"));
        Assert.Equal("2026-09-20", ClockFormats.Format(now, "yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("HH:mm:ss", true)]
    [InlineData("HH:mm", false)]
    [InlineData("h:mm tt", false)]
    [InlineData("HH:mm.fff", true)]
    [InlineData("HH:mm 'seconds'", false)]     // 's' inside a literal doesn't count
    [InlineData("HH:mm \\s", false)]            // escaped 's' doesn't count
    [InlineData("dddd, d MMMM yyyy", false)]
    public void HasSeconds_DetectsTokensOutsideLiterals(string format, bool expected)
    {
        Assert.Equal(expected, ClockFormats.HasSeconds(format));
    }
}
