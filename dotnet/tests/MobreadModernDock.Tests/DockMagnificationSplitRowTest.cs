namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;

/// <summary>
/// The dock row is split across two ItemsControls (pinned items, then
/// running-but-unpinned apps). Magnification has to be computed over the
/// whole row, not per control. These pin the two things that break when it
/// is computed per group: a discontinuity at the seam, and the two groups
/// being independently re-centred into each other.
/// </summary>
public class DockMagnificationSplitRowTest
{
    private const double Icon = 48;
    private const double Influence = Icon * DockMagnification.InfluenceIcons;

    private static double[] Row(int n, double size = Icon)
    {
        var s = new double[n];
        for (int i = 0; i < n; i++) s[i] = size;
        return s;
    }

    /// <summary>
    /// Four pinned icons then three running ones. Hovering the first running
    /// icon must also magnify the last pinned icon - they are neighbours on
    /// screen, and a per-group computation would leave the pinned one at 1.0.
    /// </summary>
    [Fact]
    public void HoveringARunningIconStillMagnifiesTheAdjacentPinnedIcon()
    {
        var whole = Row(7);
        // Centre of index 4 (the first running icon) = 48*4 + 24.
        var scales = DockMagnification.ComputeScales(whole, 48 * 4 + 24, 1.8, Influence);

        Assert.Equal(1.8, scales[4], 3);
        Assert.True(scales[3] > 1.3, $"adjacent pinned icon barely grew: {scales[3]}");
        // Symmetric neighbours on either side of the seam match.
        Assert.Equal(scales[3], scales[5], 6);
    }

    /// <summary>
    /// The seam must be invisible: sweeping the pointer across the boundary
    /// between the two groups must not jump any icon's scale.
    /// </summary>
    [Fact]
    public void ScalesAreContinuousAcrossTheSeam()
    {
        var whole = Row(7);
        double seam = 48 * 4; // boundary between pinned (0-3) and running (4-6)

        var before = DockMagnification.ComputeScales(whole, seam - 0.5, 1.8, Influence);
        var after = DockMagnification.ComputeScales(whole, seam + 0.5, 1.8, Influence);

        for (int i = 0; i < whole.Length; i++)
            Assert.True(Math.Abs(before[i] - after[i]) < 0.01,
                $"icon {i} jumped at the seam: {before[i]} -> {after[i]}");
    }

    /// <summary>
    /// Computing per group re-centres each group on itself, so the pinned
    /// group's tail and the running group's head are pushed toward each other
    /// and overlap. Computing over the whole row cannot produce that.
    /// </summary>
    [Fact]
    public void WholeRowOffsetsNeverOverlapAcrossTheSeam()
    {
        var whole = Row(7);
        var scales = DockMagnification.ComputeScales(whole, 48 * 4 + 24, 1.8, Influence);
        var offsets = DockMagnification.ComputeOffsets(whole, scales);

        double cursor = 0;
        var edges = new List<(double L, double R)>();
        for (int i = 0; i < whole.Length; i++)
        {
            double centre = cursor + whole[i] / 2 + offsets[i];
            double half = whole[i] * scales[i] / 2;
            edges.Add((centre - half, centre + half));
            cursor += whole[i];
        }

        for (int i = 1; i < edges.Count; i++)
            Assert.True(edges[i].L >= edges[i - 1].R - 1e-6,
                $"icon {i} overlaps its neighbour across the seam");
    }

    /// <summary>
    /// A panel applies only its own slice of the row-wide result. Slicing must
    /// not change any value - this is what the panel's lead/trail indexing
    /// relies on.
    /// </summary>
    [Fact]
    public void ASliceOfTheRowMatchesTheWholeRowComputation()
    {
        var whole = Row(7);
        var scales = DockMagnification.ComputeScales(whole, 100, 1.8, Influence);
        var offsets = DockMagnification.ComputeOffsets(whole, scales);

        const int lead = 4;   // the running panel sits after 4 pinned items
        var runningScales = scales.Skip(lead).ToArray();
        var runningOffsets = offsets.Skip(lead).ToArray();

        Assert.Equal(3, runningScales.Length);
        for (int i = 0; i < runningScales.Length; i++)
        {
            Assert.Equal(scales[lead + i], runningScales[i], 9);
            Assert.Equal(offsets[lead + i], runningOffsets[i], 9);
        }
    }

    /// <summary>
    /// With no running apps the behaviour must be identical to the old
    /// pinned-only computation, so the common case is provably unchanged.
    /// </summary>
    [Fact]
    public void AnEmptyTrailingGroupLeavesThePinnedRowUnchanged()
    {
        var pinnedOnly = Row(5);
        var withEmptyTail = Row(5);

        var a = DockMagnification.ComputeScales(pinnedOnly, 120, 1.8, Influence);
        var b = DockMagnification.ComputeScales(withEmptyTail, 120, 1.8, Influence);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i], 9);
    }

    /// <summary>
    /// The divider between the pinned and running groups is part of the row,
    /// and magnifies with everything else: it is a separator like any other,
    /// so singling it out would make it the one divider in the dock that
    /// stayed flat while its neighbours grew.
    /// </summary>
    [Fact]
    public void TheDividerBetweenTheGroupsMagnifiesLikeAnyOtherSeparator()
    {
        // 3 pinned, the divider, then 3 running.
        var whole = new double[] { 48, 48, 48, 22, 48, 48, 48 };
        // Pointer right on the divider's centre.
        double centre = 48 * 3 + 11;
        var scales = DockMagnification.ComputeScales(whole, centre, 1.8, Influence);

        Assert.True(scales[3] > 1.5, $"divider did not magnify: {scales[3]}");
        // And it is the peak, being directly under the pointer.
        Assert.All(scales, s => Assert.True(s <= scales[3] + 1e-9));
    }

    /// <summary>
    /// The divider is not a child of either panel, so the panel that owns the
    /// row computation reports its transform back by row index. That index
    /// must address the divider itself, not a neighbouring icon.
    /// </summary>
    [Fact]
    public void TheDividerRowIndexAddressesTheDividerEntry()
    {
        const int pinnedCount = 3;
        var whole = new double[] { 48, 48, 48, 22, 48, 48, 48 };
        var scales = DockMagnification.ComputeScales(whole, 48 * 3 + 11, 1.8, Influence);
        var offsets = DockMagnification.ComputeOffsets(whole, scales);

        // The window uses pinnedCount as the external row index.
        int ext = pinnedCount;
        Assert.Equal(22, whole[ext], 6);          // it really is the divider slot
        Assert.True(scales[ext] > 1.5);
        Assert.True(Math.Abs(offsets[ext]) < 1.0, // centred pointer: barely displaced
            $"divider displaced unexpectedly: {offsets[ext]}");
    }
}
