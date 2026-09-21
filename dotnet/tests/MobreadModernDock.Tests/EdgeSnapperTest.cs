using Avalonia;
using MobreadModernDock.Views;
using Xunit;

namespace MobreadModernDock.Tests;

public class EdgeSnapperTest
{
    private static readonly PixelRect Work = new(0, 0, 2560, 1400);

    [Fact]
    public void FarFromEverything_Unchanged()
    {
        var r = new PixelRect(700, 500, 400, 100);
        Assert.Equal(new PixelPoint(700, 500), EdgeSnapper.Snap(r, Work));
    }

    [Fact]
    public void NearBottomEdge_SnapsFlushWithMargin()
    {
        var r = new PixelRect(700, 1400 - 100 - 15, 400, 100); // 15px above bottom
        var p = EdgeSnapper.Snap(r, Work);
        Assert.Equal(1400 - 100 - EdgeSnapper.Margin, p.Y);
        Assert.Equal(700, p.X);
    }

    [Fact]
    public void NearLeftEdge_SnapsWithMargin()
    {
        var r = new PixelRect(10, 500, 400, 100);
        Assert.Equal(EdgeSnapper.Margin, EdgeSnapper.Snap(r, Work).X);
    }

    [Fact]
    public void NearRightEdge_SnapsWithMargin()
    {
        var r = new PixelRect(2560 - 400 - 20, 500, 400, 100);
        Assert.Equal(2560 - 400 - EdgeSnapper.Margin, EdgeSnapper.Snap(r, Work).X);
    }

    [Fact]
    public void NearHorizontalCenter_SnapsToCenter()
    {
        int centered = 2560 / 2 - 200;
        var r = new PixelRect(centered + 10, 500, 400, 100);
        Assert.Equal(centered, EdgeSnapper.Snap(r, Work).X);
    }

    [Fact]
    public void WorkAreaOffset_UsesWorkOrigin()
    {
        var work = new PixelRect(-2560, 0, 2560, 1400); // monitor left of primary
        var r = new PixelRect(-2560 + 5, 1400 - 100 - 5, 400, 100);
        var p = EdgeSnapper.Snap(r, work);
        Assert.Equal(-2560 + EdgeSnapper.Margin, p.X);
        Assert.Equal(1400 - 100 - EdgeSnapper.Margin, p.Y);
    }

    [Fact]
    public void JustOutsideThreshold_Unchanged()
    {
        var r = new PixelRect(EdgeSnapper.Threshold + 1, 500, 400, 100);
        Assert.Equal(EdgeSnapper.Threshold + 1, EdgeSnapper.Snap(r, Work).X);
    }

    [Fact]
    public void CustomMargin_IsUsedForRestingGap()
    {
        var r = new PixelRect(5, 5, 400, 100);
        Assert.Equal(new PixelPoint(0, 0), EdgeSnapper.Snap(r, Work, 0));
        Assert.Equal(new PixelPoint(20, 20), EdgeSnapper.Snap(r, Work, 20));
        var right = new PixelRect(2560 - 400 - 3, 1400 - 100 - 3, 400, 100);
        Assert.Equal(new PixelPoint(2560 - 400 - 32, 1400 - 100 - 32), EdgeSnapper.Snap(right, Work, 32));
    }
}
