namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Models;

/// <summary>
/// Settings and text resolution for the floating text widget — a second,
/// independent window that shows a short line of user-defined text (by
/// default the machine's host name). Persists through the shared DockModel.
/// </summary>
public class WidgetService
{
    private readonly DockService _dockService;
    private readonly List<Action> _listeners = new();

    public WidgetService(DockService dockService)
    {
        _dockService = dockService;
    }

    private DockModel Dock => _dockService.GetDock();

    public bool IsEnabled() => Dock.WidgetEnabled;

    public void SetEnabled(bool value)
    {
        Dock.WidgetEnabled = value;
        _dockService.SaveChanges();
        NotifyListeners();
    }

    public string GetTextTemplate() => Dock.WidgetText;

    public void SetTextTemplate(string value)
    {
        Dock.WidgetText = value ?? "";
        _dockService.SaveChanges();
        NotifyListeners();
    }

    public int GetFontSize() => Dock.WidgetFontSize;

    public void SetFontSize(int value)
    {
        Dock.WidgetFontSize = Math.Clamp(value, 8, 96);
        _dockService.SaveChanges();
        NotifyListeners();
    }

    public (double X, double Y) GetPosition() => (Dock.WidgetPositionX, Dock.WidgetPositionY);

    public void SetPosition(double x, double y)
    {
        Dock.WidgetPositionX = x;
        Dock.WidgetPositionY = y;
        _dockService.SaveChanges();
    }

    /// <summary>
    /// Resolves the template to display text: <c>{host}</c> → machine name,
    /// <c>{user}</c> → user name. An empty template falls back to the host name.
    /// </summary>
    public string ResolveText() => ResolveText(GetTextTemplate());

    public static string ResolveText(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "{host}";
        return template
            .Replace("{host}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }

    public void AddListener(Action listener) => _listeners.Add(listener);
    public void RemoveListener(Action listener) => _listeners.Remove(listener);

    private void NotifyListeners()
    {
        foreach (var listener in _listeners.ToList())
            listener();
    }
}
