namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;

/// <summary>
/// Geometry of the macOS-style magnification. These pin the properties that
/// make the effect look right - a symmetric falloff, no net drift of the row,
/// and no overlap between neighbours - rather than exact pixel values.
/// </summary>
public class DockMagnificationTest
{
    private static double[] Sizes(int count, double size = 48)
    {
        var s = new double[count];
        for (int i = 0; i < count; i++) s[i] = size;
        return s;
    }

    [Fact]
    public void HoveredIconGetsTheFullScale()
    {
        var sizes = Sizes(5);
        // Pointer exactly on the centre of item 2 (48*2 + 24).
        var scales = DockMagnification.ComputeScales(sizes, 120, 1.8, 120);

        Assert.Equal(1.8, scales[2], 3);
        // and it is the largest of them all
        Assert.All(scales, s => Assert.True(s <= scales[2] + 1e-9));
    }

    [Fact]
    public void ScaleFallsOffWithDistanceAndStopsAtTheInfluenceRadius()
    {
        var sizes = Sizes(7);
        var scales = DockMagnification.ComputeScales(sizes, 24, 2.0, 48 * 2.5);

        // Monotonically decreasing as we walk away from the hovered item.
        for (int i = 1; i < scales.Length; i++)
            Assert.True(scales[i] <= scales[i - 1] + 1e-9, $"scale rose at {i}");

        // Beyond the radius there is no magnification at all.
        Assert.Equal(1.0, scales[^1], 3);
    }

    [Fact]
    public void FalloffIsSymmetricAroundThePointer()
    {
        var sizes = Sizes(7);
        // Centre of item 3 = 48*3 + 24 = 168.
        var scales = DockMagnification.ComputeScales(sizes, 168, 1.8, 120);

        Assert.Equal(scales[2], scales[4], 6);
        Assert.Equal(scales[1], scales[5], 6);
        Assert.Equal(scales[0], scales[6], 6);
    }

    [Fact]
    public void FalloffIsFlatAtThePeakSoTheGrowthDoesNotSnap()
    {
        var sizes = Sizes(5);
        // A tiny pointer move near the peak must barely change the scale;
        // a linear falloff would change it proportionally.
        var a = DockMagnification.ComputeScales(sizes, 120, 2.0, 120)[2];
        var b = DockMagnification.ComputeScales(sizes, 122, 2.0, 120)[2];
        Assert.True(Math.Abs(a - b) < 0.001, $"peak is not flat: {a} vs {b}");
    }

    [Fact]
    public void GrowthSpreadsOutwardWithoutShiftingTheRow()
    {
        var sizes = Sizes(6);
        var scales = DockMagnification.ComputeScales(sizes, 100, 1.8, 120);
        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        // The invariant that matters is that the row keeps its centre: the
        // dock must not appear to slide sideways as the pointer moves. (The
        // offsets themselves do NOT sum to zero - with an off-centre pointer
        // the items are pushed asymmetrically, which is the point.)
        double restTotal = 0;
        foreach (var s in sizes) restTotal += s;

        double cursor = 0, firstLeft = 0, lastRight = 0;
        for (int i = 0; i < sizes.Length; i++)
        {
            double centre = cursor + sizes[i] / 2 + offsets[i];
            double half = sizes[i] * scales[i] / 2;
            if (i == 0) firstLeft = centre - half;
            if (i == sizes.Length - 1) lastRight = centre + half;
            cursor += sizes[i];
        }

        Assert.Equal(restTotal / 2, (firstLeft + lastRight) / 2, 6);
    }

    [Fact]
    public void GrowthIsSymmetricWhenThePointerIsCentred()
    {
        // With the pointer dead centre of an even row the displacement IS
        // symmetric, so here the offsets do cancel.
        var sizes = Sizes(6);
        var scales = DockMagnification.ComputeScales(sizes, 144, 1.8, 120);
        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        double sum = 0;
        foreach (var o in offsets) sum += o;
        Assert.Equal(0, sum, 6);

        for (int i = 0; i < 3; i++)
            Assert.Equal(-offsets[i], offsets[^(i + 1)], 6);
    }

    [Fact]
    public void NeighboursArePushedApartRatherThanOverlapping()
    {
        var sizes = Sizes(5);
        var scales = DockMagnification.ComputeScales(sizes, 120, 1.8, 120);
        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        // Rebuild each item's drawn extent and assert the gaps never invert.
        double cursor = 0;
        var edges = new List<(double L, double R)>();
        for (int i = 0; i < sizes.Length; i++)
        {
            double centre = cursor + sizes[i] / 2 + offsets[i];
            double half = sizes[i] * scales[i] / 2;
            edges.Add((centre - half, centre + half));
            cursor += sizes[i];
        }
        for (int i = 1; i < edges.Count; i++)
            Assert.True(edges[i].L >= edges[i - 1].R - 1e-6,
                $"item {i} overlaps its neighbour: {edges[i - 1].R} > {edges[i].L}");
    }

    [Fact]
    public void AVetoedItemStaysUnscaledButStillGetsPushed()
    {
        // A separator sits at index 2 and must not grow, while the icons
        // around it still magnify and shove it aside.
        var sizes = new double[] { 48, 48, 3, 48, 48 };
        var scales = DockMagnification.ComputeScales(sizes, 20, 1.8, 120);
        scales[2] = 1.0;
        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        Assert.Equal(1.0, scales[2], 6);
        // The pointer is at the left end, so the separator is pushed right.
        Assert.True(offsets[2] > 0, $"separator was not displaced: {offsets[2]}");
    }

    [Fact]
    public void MaxScaleOfOneIsAPerfectNoOp()
    {
        var sizes = Sizes(5);
        var scales = DockMagnification.ComputeScales(sizes, 120, 1.0, 120);
        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        Assert.All(scales, s => Assert.Equal(1.0, s, 6));
        Assert.All(offsets, o => Assert.Equal(0.0, o, 6));
    }

    [Fact]
    public void ScaleIsClampedToTheSupportedRange()
    {
        var sizes = Sizes(3);
        var scales = DockMagnification.ComputeScales(sizes, 24, 99.0, 120);
        Assert.Equal(DockMagnification.MaxScaleLimit, scales[0], 6);
    }

    [Fact]
    public void PointerWellOutsideTheRowIsReportedOutOfRange()
    {
        var sizes = Sizes(4); // 192 wide
        Assert.True(DockMagnification.IsOutOfRange(sizes, -500, 120));
        Assert.True(DockMagnification.IsOutOfRange(sizes, 900, 120));
        Assert.False(DockMagnification.IsOutOfRange(sizes, 100, 120));
    }
}
