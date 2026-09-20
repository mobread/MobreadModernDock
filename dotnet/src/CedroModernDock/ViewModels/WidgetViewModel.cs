using System;
using Avalonia;
using Avalonia.Media;
using CedroModernDock.Core.Application;

namespace CedroModernDock.ViewModels;

/// <summary>
/// View model for the floating text widget. Mirrors the dock's appearance
/// (color, transparency, rounding) and re-resolves the text whenever widget
/// settings or the dock appearance change.
/// </summary>
public class WidgetViewModel : ViewModelBase
{
    private readonly AppServices _appServices;

    private string _text = "";
    public string Text { get => _text; set => SetProperty(ref _text, value); }

    private double _fontSize = 14;
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

    private IBrush _background = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0));
    public IBrush Background { get => _background; set => SetProperty(ref _background, value); }

    private CornerRadius _cornerRadius = new(10);
    public CornerRadius CornerRadius { get => _cornerRadius; set => SetProperty(ref _cornerRadius, value); }

    public WidgetViewModel(AppServices appServices)
    {
        _appServices = appServices;
        _appServices.WidgetService.AddListener(Refresh);
        Refresh();
    }

    /// <summary>Re-reads text, font size and dock appearance into the bound properties.</summary>
    public void Refresh()
    {
        var widget = _appServices.WidgetService;
        var appearance = _appServices.AppearanceService;

        Text = widget.ResolveText();
        FontSize = widget.GetFontSize();
        CornerRadius = new CornerRadius(appearance.GetDockBorderRounding());

        double transparency = appearance.GetDockTransparencyPercentage() / 100.0;
        byte alpha = (byte)(transparency * 255);
        var parts = appearance.GetDockColorRGB()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        Background = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    public void Shutdown() => _appServices.WidgetService.RemoveListener(Refresh);
}
