namespace MobreadModernDock.Core.Application;

/// <summary>
/// Title and contrast logic for the window-preview popup.
/// Pure functions — no UI dependency.
/// </summary>
public static class WindowTitleFormatter
{
    public const int MaxTitleLength = 40;

    public static string Format(string? windowTitle, string? appLabel)
        => Truncate(StripRedundantAppSuffix(windowTitle, appLabel), MaxTitleLength);

    public static string StripRedundantAppSuffix(string? windowTitle, string? appLabel)
    {
        if (string.IsNullOrEmpty(windowTitle)) return "";
        if (string.IsNullOrEmpty(appLabel)) return windowTitle;

        string suffix = " - " + appLabel;
        if (windowTitle.Length >= suffix.Length &&
            windowTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = windowTitle[..^suffix.Length].Trim();
            if (!string.IsNullOrEmpty(stripped)) return stripped;
        }
        return windowTitle;
    }

    public static string Truncate(string? title, int maxLength)
    {
        if (string.IsNullOrEmpty(title)) return "";
        if (title.Length <= maxLength) return title;
        return title[..maxLength] + "...";
    }

    /// <summary>
    /// True when the dock background RGB is dark enough to need white text
    /// (brightness threshold 128).
    /// </summary>
    public static bool IsDarkBackground(string? colorRgb)
    {
        if (string.IsNullOrWhiteSpace(colorRgb)) return true;
        var parts = colorRgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            byte.TryParse(parts[0], out byte r) &&
            byte.TryParse(parts[1], out byte g) &&
            byte.TryParse(parts[2], out byte b))
        {
            double brightness = r * 0.299 + g * 0.587 + b * 0.114;
            return brightness <= 128;
        }
        return true;
    }
}
