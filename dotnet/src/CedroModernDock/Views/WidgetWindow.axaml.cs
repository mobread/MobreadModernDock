using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CedroModernDock.Core.Application;
using CedroModernDock.Infrastructure.Windows.Native;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Views;

/// <summary>
/// Floating text widget: a borderless, always-draggable window that displays
/// user-defined text (host name by default). Shares the dock's Win32 behavior
/// (no-activate, no taskbar entry, survives Win+D) and persists its position.
/// </summary>
public partial class WidgetWindow : Window
{
    private DockWindowBehavior? _behavior;
    private AppServices? _appServices;
    private readonly DispatcherTimer _positionPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

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
            if (_appServices == null) return;
            var (x, y) = _behavior?.GetScreenPosition() ?? (Position.X, Position.Y);
            _appServices.WidgetService.SetPosition(x, y);
        };
    }

    public void SetAppServices(AppServices appServices) => _appServices = appServices;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        IPlatformHandle? handle = this.TryGetPlatformHandle();
        if (handle != null)
        {
            _behavior = new DockWindowBehavior(handle.Handle);
            _behavior.Apply();
        }

        if (_appServices != null)
        {
            var (x, y) = _appServices.WidgetService.GetPosition();
            // First run: no saved position yet — place it on the primary
            // monitor's work area rather than at the virtual-desktop origin.
            if (x == 40 && y == 40)
            {
                var bounds = _appServices.PositioningService.GetPrimaryScreenBounds();
                x = bounds.MinX + 40;
                y = bounds.MinY + 40;
            }
            if (_behavior != null)
                _behavior.MoveToScreen((int)x, (int)y);
            else
                Position = new PixelPoint((int)x, (int)y);
        }

        (DataContext as WidgetViewModel)?.Refresh();
    }

    /// <summary>The widget is always freely draggable.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionPersistTimer.Stop();
        (DataContext as WidgetViewModel)?.Shutdown();
        _behavior?.Dispose();
        _behavior = null;
        base.OnClosed(e);
    }
}
