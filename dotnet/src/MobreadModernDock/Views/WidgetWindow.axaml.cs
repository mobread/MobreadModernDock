using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Native;
using MobreadModernDock.Widgets;

namespace MobreadModernDock.Views;

/// <summary>
/// Generic floating widget host: a borderless, draggable window with the
/// dock's chrome (color, transparency, rounding) around provider-supplied
/// content. Shares the dock's Win32 behavior (no-activate, no taskbar entry,
/// survives Win+D) and persists its own position per widget id.
/// </summary>
public partial class WidgetWindow : Window
{
    private DockWindowBehavior? _behavior;
    private AppServices? _appServices;
    private WidgetDefinition? _definition;
    private readonly DispatcherTimer _positionPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DockAutoHideController? _autoHide;

    /// <summary>
    /// Auto-hide only makes sense when the widget is flush with a screen edge:
    /// within the edge-snap margin (plus a little slack) on at least one side
    /// of the work area of the monitor it sits on.
    /// </summary>
    public static bool IsOnScreenEdge(PixelRect rect, int snapMargin)
    {
        var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        int slack = snapMargin + 4;
        return rect.X - work.X <= slack || work.Right - rect.Right <= slack
            || rect.Y - work.Y <= slack || work.Bottom - rect.Bottom <= slack;
    }

    public bool IsOnScreenEdge() => _appServices != null
        && IsOnScreenEdge(ScreenGeometry.WindowScreenRect(this), _appServices.AppearanceService.GetEdgeSnapMargin());

    private void ApplyAutoHideSetting()
    {
        if (_appServices == null || _definition == null || _behavior == null) return;
        bool wanted = _definition.GetSettingBool(CommonWidgetSettings.AutoHide, false) && IsOnScreenEdge();
        if (wanted && _autoHide == null)
        {
            _autoHide = new DockAutoHideController(
                behavior: () => _behavior,
                // Physical pixels, like everything else the controller compares against.
                size: () => { var r = ScreenGeometry.WindowScreenRect(this); return (r.Width, r.Height); },
                restPosition: () => ((int)_definition.PositionX, (int)_definition.PositionY),
                screenBounds: () =>
                {
                    // Hide against the physical monitor edge, not the work
                    // area: when the dock reserves its screen edge (or the
                    // taskbar is present) the work area stops short of the
                    // real edge by that strip's thickness, and a widget that
                    // slid only that far would leave a strip's worth of itself
                    // on screen - roughly one row of tray icons.
                    var (x, y) = ((int)_definition.PositionX, (int)_definition.PositionY);
                    var r = ScreenGeometry.WindowScreenRect(this);
                    var m = ScreenGeometry.MonitorAreaAt(new PixelPoint(x + r.Width / 2, y + r.Height / 2));
                    return (m.X, m.Y, m.Right, m.Bottom);
                },
                blockHide: () => false);
        }
        _autoHide?.SetEnabled(wanted);
    }

    public WidgetWindow()
    {
        InitializeComponent();
        PositionChanged += (_, _) =>
        {
            _positionPersistTimer.Stop();
            _positionPersistTimer.Start();
        };
        _positionPersistTimer.Tick += (_, _) =>
        {
            _positionPersistTimer.Stop();
            if (_appServices == null || _definition == null) return;
            // Auto-hide moves the window itself; those aren't user drags.
            if (_autoHide is { IsEnabled: true }) return;
            var (x, y) = _behavior?.GetScreenPosition() ?? (Position.X, Position.Y);
            if (_appServices.AppearanceService.GetEdgeSnapping() && _behavior != null)
            {
                var rect = ScreenGeometry.WindowScreenRect(this);
                var work = ScreenGeometry.WorkAreaAt(new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                var snapped = EdgeSnapper.Snap(rect, work, _appServices.AppearanceService.GetEdgeSnapMargin());
                if (snapped.X != x || snapped.Y != y)
                {
                    _behavior.MoveToScreen(snapped.X, snapped.Y);
                    (x, y) = (snapped.X, snapped.Y);
                }
            }
            _appServices.WidgetService.SetPosition(_definition.Id, x, y);
            // Position changed: the widget may have moved on/off an edge.
            ApplyAutoHideSetting();
        };
    }

    public string? WidgetId => _definition?.Id;

    /// <summary>Fullscreen auto-hide: show/hide the native window without changing layering.</summary>
    public void SetNativeVisible(bool visible) => _behavior?.SetNativeVisible(visible);

    /// <summary>Wires services, the definition and the provider-built content.</summary>
    public void Initialize(AppServices appServices, WidgetDefinition definition, Control content)
    {
        _appServices = appServices;
        _definition = definition;
        ContentHost.Content = content;
        ApplyChrome();
    }

    /// <summary>Re-applies dock appearance and asks the content to refresh.</summary>
    public void Refresh()
    {
        ApplyChrome();
        if (_appServices != null && _behavior != null && _definition != null)
            _behavior.SetAlwaysOnTop(CommonWidgetSettings.EffectiveAlwaysOnTop(_definition, _appServices.AppearanceService.GetAlwaysOnTop()));
        (ContentHost.Content as Control)?.DataContext.As<WidgetViewModelBase>()?.Refresh();
        ApplyAutoHideSetting();
        _autoHide?.OnLayoutChanged();
    }

    private void ApplyChrome()
    {
        if (_appServices == null) return;
        var appearance = _appServices.AppearanceService;
        Chrome.CornerRadius = new CornerRadius(appearance.GetDockBorderRounding());

        double transparency = appearance.GetDockTransparencyPercentage() / 100.0;
        byte alpha = (byte)(transparency * 255);
        var parts = appearance.GetDockColorRGB()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;

        Chrome.Background = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));

        if (_definition != null)
            Chrome.Opacity = CommonWidgetSettings.EffectiveOpacity(_definition, appearance.GetGlobalOpacityPercentage());
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        IPlatformHandle? handle = this.TryGetPlatformHandle();
        if (handle != null)
        {
            _behavior = new DockWindowBehavior(handle.Handle);
            _behavior.Apply(_appServices != null && _definition != null
                && CommonWidgetSettings.EffectiveAlwaysOnTop(_definition, _appServices.AppearanceService.GetAlwaysOnTop()));
        }

        if (_appServices != null && _definition != null)
        {
            double x, y;
            if (_definition.HasPosition)
            {
                (x, y) = (_definition.PositionX, _definition.PositionY);
            }
            else
            {
                // First show: place on the primary monitor's work area,
                // staggered so several new widgets don't stack exactly.
                var bounds = _appServices.PositioningService.GetPrimaryScreenBounds();
                int index = Math.Max(0, IndexOfDefinition());
                x = bounds.MinX + 40 + index * 30;
                y = bounds.MinY + 40 + index * 30;
            }
            if (_behavior != null)
                _behavior.MoveToScreen((int)x, (int)y);
            else
                Position = new PixelPoint((int)x, (int)y);
        }

        Refresh();
    }

    private int IndexOfDefinition()
    {
        if (_appServices == null || _definition == null) return 0;
        int i = 0;
        foreach (var w in _appServices.WidgetService.GetWidgets())
        {
            if (w.Id == _definition.Id) return i;
            i++;
        }
        return 0;
    }

    /// <summary>
    /// Drag the window from its background. Presses on interactive content
    /// (buttons inside the widget) are left to the content.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;
        BeginMoveDrag(e);
    }

    private readonly DismissableMenu _menu = new();

    /// <summary>
    /// Right-click on the widget's chrome: open its settings, or disable it.
    /// A press on a button inside the widget (a tray icon, a quick-launch
    /// entry) belongs to that content and its own menu, so it is left alone.
    /// The window is WS_EX_NOACTIVATE like the dock, hence the dismissable
    /// menu rather than a plain ContextMenu.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_appServices == null || _definition == null) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;
        e.Handled = true;

        var loc = _appServices.LocalizationService;
        var menu = new ContextMenu();

        var settings = new MenuItem { Header = loc.Text("widget.context.settings") };
        settings.Click += (_, _) => App.OpenWidgetSettings(_definition.Id);
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var disable = new MenuItem { Header = loc.Text("widget.context.disable") };
        // The service notifies App, which closes this window; nothing else
        // to do here, and the definition stays in Settings for re-enabling.
        disable.Click += (_, _) => _appServices.WidgetService.SetEnabled(_definition.Id, false);
        menu.Items.Add(disable);

        _menu.Open(menu, Chrome);
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionPersistTimer.Stop();
        _menu.Dispose();
        _autoHide?.Dispose();
        _autoHide = null;
        (ContentHost.Content as Control)?.DataContext.As<WidgetViewModelBase>()?.Shutdown();
        _behavior?.Dispose();
        _behavior = null;
        base.OnClosed(e);
    }
}

internal static class ObjectExtensions
{
    public static T? As<T>(this object? o) where T : class => o as T;
}
