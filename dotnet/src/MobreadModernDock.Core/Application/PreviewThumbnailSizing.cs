namespace MobreadModernDock.Core.Application;

/// <summary>
/// Geometry of the hover window-preview rows. Pure so it can be tested; the
/// popup feeds in the user's size setting and the room it has on screen.
/// All values are DIPs.
/// </summary>
public static class PreviewThumbnailSizing
{
    public const double BaseWidth = 144;
    public const double BaseHeight = 81;

    /// <summary>Row chrome around one thumbnail: top padding + title line (4 + ~16 + 4).</summary>
    public const double RowVerticalChrome = 6 + 24;
    /// <summary>Row horizontal padding (6 + 6).</summary>
    public const double RowHorizontalChrome = 12;
    /// <summary>Gap between rows (StackPanel.Spacing).</summary>
    public const double RowSpacing = 6;
    /// <summary>Panel margin around all rows (4 + 4).</summary>
    public const double PanelChrome = 8;

    /// <summary>Never shrink below this, even when the screen is tiny.</summary>
    public const double MinHeight = 36;

    /// <summary>
    /// Thumbnail size for <paramref name="percent"/> of the stock size,
    /// shrunk (aspect kept) so <paramref name="rowCount"/> stacked rows fit in
    /// <paramref name="availableHeight"/> and one row in
    /// <paramref name="availableWidth"/>. Pass <c>double.PositiveInfinity</c>
    /// for an unconstrained axis.
    /// </summary>
    public static (double Width, double Height) Fit(
        int percent, int rowCount, double availableWidth, double availableHeight)
    {
        double height = BaseHeight * percent / 100.0;
        int rows = Math.Max(1, rowCount);

        double fitHeight = (availableHeight - PanelChrome - (rows - 1) * RowSpacing) / rows - RowVerticalChrome;
        double fitWidthAsHeight = (availableWidth - PanelChrome - RowHorizontalChrome) * BaseHeight / BaseWidth;
        height = Math.Min(height, Math.Min(fitHeight, fitWidthAsHeight));
        height = Math.Max(MinHeight, height);

        height = Math.Floor(height);
        double width = Math.Round(height * BaseWidth / BaseHeight);
        return (width, height);
    }

    /// <summary>Longest title: 75 % of the row width, so a long title never widens the popup.</summary>
    public static double TitleMaxWidth(double thumbWidth) =>
        Math.Floor((thumbWidth + RowHorizontalChrome + PanelChrome) * 0.75);
}
