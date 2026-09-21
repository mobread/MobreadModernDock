namespace MobreadModernDock.Core.Application;

using System;

/// <summary>
/// macOS-style dock magnification: icons near the pointer grow, and the growth
/// pushes their neighbours outward so nothing overlaps.
///
/// Pure geometry along a single axis - the caller supplies rest sizes measured
/// along the dock's main axis (widths for a horizontal dock, heights for a
/// vertical one) and gets back a scale and a displacement per item. Only
/// single-line docks use this; wrapping rows would need a 2D falloff and the
/// pushed items would collide across lines.
/// </summary>
public static class DockMagnification
{
    /// <summary>Largest magnification allowed, to keep the slider sane.</summary>
    public const double MaxScaleLimit = 2.5;

    /// <summary>
    /// How far the effect reaches, as a multiple of one icon's size. macOS
    /// feels like roughly two and a half icons either side.
    /// </summary>
    public const double InfluenceIcons = 2.5;

    /// <summary>
    /// Per-item scale factors for a pointer at <paramref name="pointer"/>
    /// (same axis and origin as the sizes). Items further than the influence
    /// radius are left at 1.
    ///
    /// The falloff is a raised cosine, which is flat at both the peak and the
    /// edge of the influence radius: a linear falloff visibly "kinks" as an
    /// icon enters range, and a bare cosine kinks at the outer edge.
    /// </summary>
    public static double[] ComputeScales(
        IReadOnlyList<double> sizes, double pointer, double maxScale, double influence)
    {
        var scales = new double[sizes.Count];
        if (sizes.Count == 0) return scales;

        maxScale = Math.Clamp(maxScale, 1.0, MaxScaleLimit);
        double cursor = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            double centre = cursor + sizes[i] / 2;
            cursor += sizes[i];

            if (influence <= 0 || maxScale <= 1.0)
            {
                scales[i] = 1.0;
                continue;
            }

            double t = Math.Min(1.0, Math.Abs(pointer - centre) / influence);
            // Hann window: 1 at t=0, 0 at t=1, zero slope at both ends.
            double falloff = 0.5 * (1.0 + Math.Cos(t * Math.PI));
            scales[i] = 1.0 + (maxScale - 1.0) * falloff;
        }
        return scales;
    }

    /// <summary>
    /// Displacement per item once every item is drawn at its scaled size,
    /// keeping the row centred: the growth spreads symmetrically outward from
    /// the middle instead of shunting the whole row sideways.
    ///
    /// Scales are taken as a parameter rather than recomputed so the caller can
    /// veto magnification for particular items (separators) and still have the
    /// remaining items pushed correctly around them.
    /// </summary>
    public static double[] ComputeOffsets(IReadOnlyList<double> sizes, IReadOnlyList<double> scales)
    {
        int n = sizes.Count;
        var offsets = new double[n];
        if (n == 0) return offsets;

        double restTotal = 0, scaledTotal = 0;
        for (int i = 0; i < n; i++)
        {
            restTotal += sizes[i];
            scaledTotal += sizes[i] * scales[i];
        }

        double restCursor = 0, scaledCursor = 0;
        for (int i = 0; i < n; i++)
        {
            double restCentre = restCursor + sizes[i] / 2 - restTotal / 2;
            double scaledCentre = scaledCursor + sizes[i] * scales[i] / 2 - scaledTotal / 2;
            offsets[i] = scaledCentre - restCentre;
            restCursor += sizes[i];
            scaledCursor += sizes[i] * scales[i];
        }
        return offsets;
    }

    /// <summary>
    /// How far the row's outermost edge is pushed beyond its rest bounds, per
    /// side, at the worst pointer position.
    ///
    /// <see cref="ComputeOffsets"/> keeps the row centred, so the leftmost
    /// point sits at -scaledTotal/2 while at rest it sits at -restTotal/2:
    /// the end items therefore overhang by exactly half the row's expansion.
    /// The window has to reserve that much transparent headroom on each side,
    /// because a window cannot draw outside its own bounds - without it the
    /// end icons are sliced off at the window edge no matter what
    /// <c>ClipToBounds</c> says.
    ///
    /// Returns 0 when nothing would magnify. The result is a *worst case* over
    /// every pointer position, so it is computed once when the items or the
    /// settings change rather than on each pointer move.
    /// </summary>
    public static double MaxOverhang(
        IReadOnlyList<double> sizes, double maxScale, double influence)
    {
        int n = sizes.Count;
        if (n == 0 || influence <= 0) return 0;
        maxScale = Math.Clamp(maxScale, 1.0, MaxScaleLimit);
        if (maxScale <= 1.0) return 0;

        double restTotal = 0;
        foreach (var s in sizes) restTotal += s;
        if (restTotal <= 0) return 0;

        // Item centres, so the sweep does not recompute them per sample.
        var centres = new double[n];
        double cursor = 0;
        for (int i = 0; i < n; i++)
        {
            centres[i] = cursor + sizes[i] / 2;
            cursor += sizes[i];
        }

        // The expansion peaks somewhere in the interior (near an end, half the
        // falloff window hangs off the row and contributes nothing). Sweep in
        // steps fine enough to catch the peak without scaling with dock width.
        double step = Math.Max(1.0, restTotal / 512.0);
        double best = 0;
        for (double p = 0; p <= restTotal; p += step)
        {
            double expansion = 0;
            for (int i = 0; i < n; i++)
            {
                double t = Math.Min(1.0, Math.Abs(p - centres[i]) / influence);
                double falloff = 0.5 * (1.0 + Math.Cos(t * Math.PI));
                expansion += sizes[i] * (maxScale - 1.0) * falloff;
            }
            if (expansion > best) best = expansion;
        }
        return best / 2.0;
    }

    /// <summary>
    /// True when the pointer is far enough outside the row that nothing would
    /// be magnified, so the caller can skip the update entirely.
    /// </summary>
    public static bool IsOutOfRange(IReadOnlyList<double> sizes, double pointer, double influence)
    {
        if (sizes.Count == 0) return true;
        double total = 0;
        foreach (var s in sizes) total += s;
        return pointer < -influence || pointer > total + influence;
    }
}
