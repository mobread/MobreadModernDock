using System;
using Avalonia;
using Avalonia.Controls;

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
}
