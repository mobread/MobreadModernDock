using Avalonia.Controls;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets;

/// <summary>
/// Contract for a widget type. One provider per <see cref="WidgetTypes"/> key;
/// the host creates a <see cref="Views.WidgetWindow"/> per enabled definition
/// and asks the provider for the content to show inside it.
/// </summary>
public interface IWidgetProvider
{
    /// <summary>Stable type key stored in config (e.g. "text", "tray").</summary>
    string TypeKey { get; }

    /// <summary>Localization key for the human-readable type name.</summary>
    string DisplayNameKey { get; }

    /// <summary>Default settings for a freshly added widget of this type.</summary>
    Dictionary<string, string> DefaultSettings();

    /// <summary>
    /// Builds the content view for a widget instance. The returned control is
    /// placed inside the widget window's chrome; its DataContext should be a
    /// <see cref="WidgetViewModelBase"/> so the host can refresh/shut it down.
    /// </summary>
    Control CreateView(WidgetDefinition definition, AppServices services);

    /// <summary>
    /// Builds the per-widget settings panel shown in the Settings window's
    /// Widgets tab. Returns null when the type has no options.
    /// </summary>
    Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged);
}

/// <summary>
/// Base view model for widget content. The host calls <see cref="Refresh"/>
/// when the definition or dock appearance changes and <see cref="Shutdown"/>
/// when the window closes.
/// </summary>
public abstract class WidgetViewModelBase : ViewModels.ViewModelBase
{
    public abstract void Refresh();
    public virtual void Shutdown() { }
}
