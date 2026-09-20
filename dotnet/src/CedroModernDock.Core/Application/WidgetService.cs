namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Models;

/// <summary>
/// CRUD layer over the persisted widget list plus change notification.
/// Widget types are identified by a string key (see <see cref="WidgetTypes"/>);
/// the UI layer maps keys to providers that build the actual windows.
/// </summary>
public class WidgetService
{
    private readonly DockService _dockService;
    private readonly List<Action> _listeners = new();

    public WidgetService(DockService dockService)
    {
        _dockService = dockService;
        MigrateLegacyTextWidget();
    }

    private DockModel Dock => _dockService.GetDock();

    public IReadOnlyList<WidgetDefinition> GetWidgets() => Dock.Widgets;

    public WidgetDefinition? Find(string id) => Dock.Widgets.FirstOrDefault(w => w.Id == id);

    public WidgetDefinition Add(string type, Dictionary<string, string>? settings = null)
    {
        var def = new WidgetDefinition { Type = type };
        if (settings != null)
            foreach (var (k, v) in settings) def.Settings[k] = v;
        Dock.Widgets.Add(def);
        Save();
        return def;
    }

    public void Remove(string id)
    {
        int removed = Dock.Widgets.RemoveAll(w => w.Id == id);
        if (removed > 0) Save();
    }

    public void SetEnabled(string id, bool enabled)
    {
        var def = Find(id);
        if (def == null || def.Enabled == enabled) return;
        def.Enabled = enabled;
        Save();
    }

    /// <summary>Position updates persist without notifying: windows own their own position.</summary>
    public void SetPosition(string id, double x, double y)
    {
        var def = Find(id);
        if (def == null) return;
        def.PositionX = x;
        def.PositionY = y;
        _dockService.SaveChanges();
    }

    public void UpdateSetting(string id, string key, string value)
    {
        var def = Find(id);
        if (def == null) return;
        if (def.Settings.TryGetValue(key, out var existing) && existing == value) return;
        def.Settings[key] = value;
        Save();
    }

    public void AddListener(Action listener) => _listeners.Add(listener);
    public void RemoveListener(Action listener) => _listeners.Remove(listener);

    private void Save()
    {
        _dockService.SaveChanges();
        foreach (var listener in _listeners.ToList())
            listener();
    }

    /// <summary>
    /// Converts the pre-framework single text widget (widgetEnabled/widgetText/…)
    /// into a "text" WidgetDefinition and clears the legacy fields so they are
    /// no longer serialized.
    /// </summary>
    private void MigrateLegacyTextWidget()
    {
        var dock = Dock;
        bool hasLegacy = dock.WidgetEnabled != null || dock.WidgetText != null
                      || dock.WidgetFontSize != null || dock.WidgetPositionX != null;
        if (!hasLegacy) return;

        if (dock.WidgetEnabled == true || !string.IsNullOrEmpty(dock.WidgetText))
        {
            var def = new WidgetDefinition
            {
                Type = WidgetTypes.Text,
                Enabled = dock.WidgetEnabled ?? false,
                PositionX = dock.WidgetPositionX ?? double.NaN,
                PositionY = dock.WidgetPositionY ?? double.NaN,
            };
            def.SetSetting(TextWidgetSettings.Template, dock.WidgetText ?? "{host}");
            def.SetSetting(TextWidgetSettings.FontSize, (dock.WidgetFontSize ?? 14).ToString());
            dock.Widgets.Add(def);
        }

        dock.WidgetEnabled = null;
        dock.WidgetText = null;
        dock.WidgetFontSize = null;
        dock.WidgetPositionX = null;
        dock.WidgetPositionY = null;
        _dockService.SaveChanges();
    }

    // --- Text widget helpers (kept here so Core tests can cover them) ---

    /// <summary>
    /// Resolves a text template: <c>{host}</c> → machine name, <c>{user}</c> →
    /// user name. An empty template falls back to the host name.
    /// </summary>
    public static string ResolveText(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "{host}";
        return template
            .Replace("{host}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Settings shared by every widget type (read by the host window).</summary>
public static class CommonWidgetSettings
{
    /// <summary>"global" (default) or "custom".</summary>
    public const string OpacityMode = "opacityMode";
    /// <summary>Percentage 20..100, used when OpacityMode is "custom".</summary>
    public const string Opacity = "opacity";

    public const string OpacityModeGlobal = "global";
    public const string OpacityModeCustom = "custom";

    /// <summary>Resolves the opacity a widget should render at, given the global percentage.</summary>
    public static double EffectiveOpacity(Models.WidgetDefinition def, int globalPercentage)
    {
        bool custom = def.GetSetting(OpacityMode, OpacityModeGlobal) == OpacityModeCustom;
        int pct = custom ? def.GetSettingInt(Opacity, 100) : globalPercentage;
        return Math.Clamp(pct, 20, 100) / 100.0;
    }
}

/// <summary>Well-known widget type keys.</summary>
public static class WidgetTypes
{
    public const string Text = "text";
    public const string Tray = "tray";
    public const string Clock = "clock";
    public const string SystemMonitor = "sysmon";
}

public static class TextWidgetSettings
{
    public const string Template = "template";
    public const string FontSize = "fontSize";
}

public static class TrayWidgetSettings
{
    public const string IconSize = "iconSize";
    public const string Spacing = "spacing";
    public const string Vertical = "vertical";
    public const string ShowSystemIcons = "showSystemIcons";
}

public static class ClockWidgetSettings
{
    /// <summary>Preset key from <see cref="ClockFormats"/>, or "custom".</summary>
    public const string Preset = "preset";
    /// <summary>.NET date/time format string used when Preset is "custom".</summary>
    public const string CustomFormat = "customFormat";
    public const string FontSize = "fontSize";
    /// <summary>"true" to render the date on a second, smaller line.</summary>
    public const string TwoLines = "twoLines";
}

/// <summary>
/// Built-in clock layouts. Each preset is a .NET format string; when
/// TwoLines is on, the first line is the time and the second the date.
/// </summary>
public static class ClockFormats
{
    public const string Custom = "custom";

    public static readonly IReadOnlyList<(string Key, string TimeFormat, string? DateFormat)> Presets = new[]
    {
        ("time24",           "HH:mm",           (string?)null),
        ("time24s",          "HH:mm:ss",        null),
        ("time12",           "h:mm tt",         null),
        ("time12s",          "h:mm:ss tt",      null),
        ("time24-weekday",   "HH:mm",           "dddd"),
        ("time24-date",      "HH:mm",           "ddd, d MMM"),
        ("time24-fulldate",  "HH:mm",           "dddd, d MMMM yyyy"),
        ("time24s-iso",      "HH:mm:ss",        "yyyy-MM-dd"),
        ("time12-date",      "h:mm tt",         "dddd, MMMM d"),
        ("dateonly",         "dddd, d MMMM",    null),
    };

    public static (string TimeFormat, string? DateFormat) Resolve(string presetKey, string? customFormat)
    {
        if (presetKey == Custom)
            return (string.IsNullOrWhiteSpace(customFormat) ? "HH:mm" : customFormat, null);
        foreach (var p in Presets)
            if (p.Key == presetKey) return (p.TimeFormat, p.DateFormat);
        return (Presets[0].TimeFormat, Presets[0].DateFormat);
    }

    /// <summary>Formats safely: an invalid custom pattern falls back to HH:mm rather than throwing.</summary>
    public static string Format(DateTime now, string format)
    {
        try { return now.ToString(format, System.Globalization.CultureInfo.CurrentCulture); }
        catch (FormatException) { return now.ToString("HH:mm"); }
    }

    /// <summary>
    /// True when the format contains a seconds token (s) or sub-second (f/F)
    /// outside quoted literals and escapes, so the widget should tick every second.
    /// </summary>
    public static bool HasSeconds(string format)
    {
        char quote = '\0';
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c == '\'' || c == '"') { quote = c; continue; }
            if (c == '\\') { i++; continue; }
            if (c == 's' || c == 'f' || c == 'F') return true;
        }
        return false;
    }
}
