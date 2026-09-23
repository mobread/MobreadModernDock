namespace MobreadModernDock.Widgets;

/// <summary>Maps widget type keys to providers. Register new widget types here.</summary>
public sealed class WidgetRegistry
{
    private readonly Dictionary<string, IWidgetProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public WidgetRegistry Register(IWidgetProvider provider)
    {
        _providers[provider.TypeKey] = provider;
        return this;
    }

    public IWidgetProvider? Get(string typeKey) =>
        _providers.TryGetValue(typeKey, out var p) ? p : null;

    public IReadOnlyCollection<IWidgetProvider> All => _providers.Values;

    /// <summary>The default set of built-in widget types.</summary>
    public static WidgetRegistry CreateDefault() => new WidgetRegistry()
        .Register(new Text.TextWidgetProvider())
        .Register(new Tray.TrayWidgetProvider())
        .Register(new Clock.ClockWidgetProvider())
        .Register(new SystemMonitor.SystemMonitorWidgetProvider())
        .Register(new Media.MediaWidgetProvider())
        .Register(new Weather.WeatherWidgetProvider())
        .Register(new QuickLaunch.QuickLaunchWidgetProvider())
        .Register(new Calendar.CalendarWidgetProvider())
        .Register(new Battery.BatteryWidgetProvider())
        .Register(new VirtualDesktop.VirtualDesktopWidgetProvider());
}
