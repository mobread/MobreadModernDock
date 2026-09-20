using MobreadModernDock.Core.Application;

namespace MobreadModernDock.Tests;

public class WindowTitleFormatterTests
{
    [Fact]
    public void Format_StripsRedundantAppSuffix()
    {
        Assert.Equal("Untitled", WindowTitleFormatter.Format("Untitled - Chrome", "Chrome"));
    }

    [Fact]
    public void Format_StripsSuffixCaseInsensitively()
    {
        Assert.Equal("Untitled", WindowTitleFormatter.Format("Untitled - CHROME", "chrome"));
    }

    [Fact]
    public void Format_KeepsTitleWhenLabelDoesNotMatch()
    {
        Assert.Equal("Untitled - Chrome", WindowTitleFormatter.Format("Untitled - Chrome", "Vivaldi"));
    }

    [Fact]
    public void Format_TruncatesLongTitles()
    {
        Assert.Equal(new string('a', 40) + "...", WindowTitleFormatter.Format(new string('a', 45), "Chrome"));
    }

    [Fact]
    public void Format_HandlesNullTitle()
    {
        Assert.Equal("", WindowTitleFormatter.Format(null, "Chrome"));
    }

    [Fact]
    public void Format_DoesNotStripWhenResultWouldBeEmpty()
    {
        Assert.Equal("Chrome", WindowTitleFormatter.Format("Chrome", "Chrome"));
    }

    [Fact]
    public void IsDarkBackground_BlackReturnsTrue()
    {
        Assert.True(WindowTitleFormatter.IsDarkBackground("0, 0, 0, "));
    }

    [Fact]
    public void IsDarkBackground_WhiteReturnsFalse()
    {
        Assert.False(WindowTitleFormatter.IsDarkBackground("255, 255, 255, "));
    }

    [Fact]
    public void IsDarkBackground_RedIsDark()
    {
        Assert.True(WindowTitleFormatter.IsDarkBackground("255, 0, 0, "));
    }

    [Fact]
    public void IsDarkBackground_InvalidFallsBackToDark()
    {
        Assert.True(WindowTitleFormatter.IsDarkBackground(""));
    }
}
