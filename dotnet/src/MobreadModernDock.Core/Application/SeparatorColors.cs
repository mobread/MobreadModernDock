namespace MobreadModernDock.Core.Application;

using System.Globalization;

/// <summary>
/// Separator colours are stored as <c>#AARRGGBB</c> hex strings (alpha first,
/// like Avalonia's own colour syntax) so one value carries both the colour
/// and how faint the line is. Pure helpers: parsing, normalising, and the
/// default that reproduces the original look (white at 35 %).
/// </summary>
public static class SeparatorColors
{
    /// <summary>White at 35 % - the look separators have always had.</summary>
    public const string Default = "#59FFFFFF";

    /// <summary>
    /// Canonical <c>#AARRGGBB</c> (upper case) for <c>#RRGGBB</c> / <c>#AARRGGBB</c>
    /// input with or without the leading '#'; null for anything else, so a
    /// hand-edited config with a typo falls back instead of throwing.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string s = value.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return null;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        return "#" + s.ToUpperInvariant();
    }

    /// <summary>ARGB bytes of a colour string; the default colour when it is invalid.</summary>
    public static (byte A, byte R, byte G, byte B) ToArgb(string? value)
    {
        string hex = Normalize(value) ?? Default;
        uint v = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string FromArgb(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    /// <summary>
    /// Quick picks for a single separator's context menu: the default look
    /// first, then saturated colours at a strength that still reads as a
    /// divider rather than a stripe.
    /// </summary>
    public static readonly string[] MenuPalette =
    {
        "#59FFFFFF", // default white 35 %
        "#CCFFFFFF", // bright white
        "#59000000", // dark
        "#CCEF4444", // red
        "#CCF97316", // orange
        "#CCEAB308", // yellow
        "#CC22C55E", // green
        "#CC06B6D4", // cyan
        "#CC3B82F6", // blue
        "#CCA855F7", // purple
        "#CCEC4899", // pink
    };
}
