using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using MobreadModernDock.Core.Application;

namespace MobreadModernDock.Views;

/// <summary>
/// Lays the pinned dock items out in <see cref="Lines"/> rows (horizontal dock)
/// or columns (vertical dock), giving every child its own desired size.
///
/// This replaces a UniformGrid, which forces every cell to the size of the
/// widest child: a 2px separator therefore reserved a whole icon-width cell and
/// left a visible gap beside it. Here each line is packed at the children's real
/// sizes, and lines are centred against the longest one so a wrapped dock stays
/// symmetrical.
/// </summary>
public class DockItemsPanel : Panel
{
    public static readonly StyledProperty<int> LinesProperty =
        AvaloniaProperty.Register<DockItemsPanel, int>(nameof(Lines), 1);

    public static readonly StyledProperty<bool> IsVerticalProperty =
        AvaloniaProperty.Register<DockItemsPanel, bool>(nameof(IsVertical));

    static DockItemsPanel()
    {
        AffectsMeasure<DockItemsPanel>(LinesProperty, IsVerticalProperty);
        AffectsArrange<DockItemsPanel>(LinesProperty, IsVerticalProperty);
    }

    /// <summary>Rows for a horizontal dock, columns for a vertical one. 1 = a single line.</summary>
    public int Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>True when the dock runs top-to-bottom; swaps the two axes.</summary>
    public bool IsVertical
    {
        get => GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    // --- macOS-style magnification ---

    /// <summary>
    /// Peak magnification under the pointer. 1.0 disables the effect. Only
    /// honoured while <see cref="Lines"/> is 1: with wrapped rows the
    /// pushed-apart icons would collide across lines.
    /// </summary>
    public double MagnifyScale { get; set; } = 1.0;

    /// <summary>
    /// Pointer position along the dock's main axis, in this panel's
    /// coordinates, or null when the pointer is away. Set by the window.
    /// </summary>
    public double? MagnifyPointer { get; set; }

    private bool MagnificationActive =>
        Lines <= 1 && MagnifyScale > 1.0 && MagnifyPointer.HasValue;

    /// <summary>
    /// Re-runs arrange with a new pointer position. Cheaper than a full
    /// InvalidateMeasure: the rest sizes have not changed, only where each
    /// item is drawn.
    /// </summary>
    public void UpdateMagnification(double? pointerOnMainAxis)
    {
        if (MagnifyPointer.Equals(pointerOnMainAxis)) return;
        MagnifyPointer = pointerOnMainAxis;
        InvalidateArrange();
    }

    /// <summary>
    /// How many items sit on each line. Items fill line 0 first, matching the
    /// order the UniformGrid used, so a wrapped dock reads the same way.
    /// </summary>
    private int PerLine(int count)
    {
        int lines = Math.Max(1, Lines);
        return Math.Max(1, (count + lines - 1) / lines);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Children.Count;
        if (count == 0) return default;

        foreach (var child in Children)
            child.Measure(Size.Infinity);

        int perLine = PerLine(count);
        double longest = 0, thickness = 0;

        for (int start = 0; start < count; start += perLine)
        {
            double along = 0, across = 0;
            int end = Math.Min(start + perLine, count);
            for (int i = start; i < end; i++)
            {
                var size = Children[i].DesiredSize;
                // "Along" runs down the line, "across" is the line's thickness.
                along += IsVertical ? size.Height : size.Width;
                across = Math.Max(across, IsVertical ? size.Width : size.Height);
            }
            longest = Math.Max(longest, along);
            thickness += across;
        }

        return IsVertical ? new Size(thickness, longest) : new Size(longest, thickness);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = Children.Count;
        if (count == 0) return finalSize;

        if (MagnificationActive)
            return ArrangeMagnified(finalSize);

        // Clear any transform left over from a previous magnified pass.
        foreach (var child in Children)
            child.RenderTransform = null;

        int perLine = PerLine(count);
        double lineOffset = 0;

        for (int start = 0; start < count; start += perLine)
        {
            int end = Math.Min(start + perLine, count);

            double along = 0, across = 0;
            for (int i = start; i < end; i++)
            {
                var size = Children[i].DesiredSize;
                along += IsVertical ? size.Height : size.Width;
                across = Math.Max(across, IsVertical ? size.Width : size.Height);
            }

            // Centre this line against the panel, so a short wrapped row sits
            // under the middle of the long one rather than hugging the start.
            double total = IsVertical ? finalSize.Height : finalSize.Width;
            double cursor = Math.Max(0, (total - along) / 2);

            for (int i = start; i < end; i++)
            {
                var child = Children[i];
                var size = child.DesiredSize;
                if (IsVertical)
                {
                    // Centre each item across the line's thickness too, so a
                    // narrow separator lines up with the icons beside it.
                    double x = lineOffset + Math.Max(0, (across - size.Width) / 2);
                    child.Arrange(new Rect(x, cursor, size.Width, size.Height));
                    cursor += size.Height;
                }
                else
                {
                    double y = lineOffset + Math.Max(0, (across - size.Height) / 2);
                    child.Arrange(new Rect(cursor, y, size.Width, size.Height));
                    cursor += size.Width;
                }
            }

            lineOffset += across;
        }

        return finalSize;
    }

    /// <summary>
    /// Single-line arrange with macOS magnification: every child keeps its
    /// rest slot in the layout, and the growth is applied as a render
    /// transform so it costs no re-measure and cannot reflow the window.
    ///
    /// Icons scale about their *outer* edge rather than their centre, so they
    /// grow away from the screen edge the dock sits on - the macOS behaviour,
    /// and the reason a magnified icon never gets clipped by the dock's own
    /// bounds. Separators are excluded from the growth but still slide.
    /// </summary>
    private Size ArrangeMagnified(Size finalSize)
    {
        int count = Children.Count;
        var sizes = new double[count];
        for (int i = 0; i < count; i++)
        {
            var d = Children[i].DesiredSize;
            sizes[i] = IsVertical ? d.Height : d.Width;
        }

        double influence = 0;
        foreach (var s in sizes) influence = Math.Max(influence, s);
        influence *= DockMagnification.InfluenceIcons;

        double pointer = MagnifyPointer!.Value;
        var scales = DockMagnification.ComputeScales(sizes, pointer, MagnifyScale, influence);

        // A separator must not balloon; it still gets pushed by its neighbours.
        for (int i = 0; i < count; i++)
            if (IsSeparatorChild(Children[i])) scales[i] = 1.0;

        var offsets = DockMagnification.ComputeOffsets(sizes, scales);

        double total = 0;
        foreach (var s in sizes) total += s;
        double start = Math.Max(0, ((IsVertical ? finalSize.Height : finalSize.Width) - total) / 2);

        double cursor = start;
        for (int i = 0; i < count; i++)
        {
            var child = Children[i];
            var size = child.DesiredSize;

            if (IsVertical)
            {
                double x = Math.Max(0, (finalSize.Width - size.Width) / 2);
                child.Arrange(new Rect(x, cursor, size.Width, size.Height));
                cursor += size.Height;
            }
            else
            {
                double y = Math.Max(0, (finalSize.Height - size.Height) / 2);
                child.Arrange(new Rect(cursor, y, size.Width, size.Height));
                cursor += size.Width;
            }

            child.RenderTransform = BuildTransform(scales[i], offsets[i], IsVertical);
            // Anchor the scale to the edge the dock rests against: bottom for a
            // horizontal dock, right for a vertical one on the left, etc. Using
            // the centre would make icons grow into the screen edge instead.
            child.RenderTransformOrigin = IsVertical
                ? new RelativePoint(1, 0.5, RelativeUnit.Relative)
                : new RelativePoint(0.5, 1, RelativeUnit.Relative);
        }

        return finalSize;
    }

    private static ITransform BuildTransform(double scale, double offset, bool vertical)
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        // The displacement runs along the dock's main axis.
        group.Children.Add(vertical
            ? new TranslateTransform(0, offset)
            : new TranslateTransform(offset, 0));
        return group;
    }

    /// <summary>
    /// Separators opt out of magnification. They are tagged by the item
    /// template's style class rather than by type, so the panel needs no
    /// reference to the view models.
    /// </summary>
    private static bool IsSeparatorChild(Control child) =>
        child.Classes.Contains("separator")
        || (child is ContentPresenter { Child: Control inner } && inner.Classes.Contains("separator"));
}
