namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;

/// <summary>
/// Pure formatting for the battery widget, kept out of the view so it can
/// be unit-tested: label text and the "time left" rendering.
/// </summary>
public static class BatteryFormats
{
    /// <summary>"1h 23m" / "45m"; null when there is nothing to say.</summary>
    public static string? FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is not { } t || t <= TimeSpan.Zero) return null;
        int h = (int)t.TotalHours, m = t.Minutes;
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    /// <summary>Clamp a percentage into 0..100 (the API reports 255 for unknown).</summary>
    public static int ClampPercent(int raw) => raw is < 0 or > 100 ? 0 : raw;

    /// <summary>Second-line status: charging / plugged in / time left / nothing.</summary>
    public static string StatusLine(BatteryStatus s, bool showTime, Func<string, string> text)
    {
        if (!s.HasBattery) return "";
        if (s.IsCharging) return text("widget.battery.charging");
        if (s.IsPluggedIn) return text("widget.battery.pluggedIn");
        if (showTime && FormatRemaining(s.TimeRemaining) is { } left)
            return string.Format(text("widget.battery.remaining"), left);
        return "";
    }
}
