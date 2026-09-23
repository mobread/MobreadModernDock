using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.Battery;

/// <summary>
/// Battery widget: a drawn battery glyph filled to the charge level, the
/// percentage beside it and an optional status line (charging / plugged in
/// / time left). On a desktop without a battery it says so and stops.
/// </summary>
public sealed class BatteryWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Battery;
    public string DisplayNameKey => "widget.type.battery";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [BatteryWidgetSettings.ShowPercent] = "true",
        [BatteryWidgetSettings.ShowTime] = "true",
        [BatteryWidgetSettings.FontSize] = "14",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new BatteryWidgetViewModel(definition, services.BatteryGateway, services.LocalizationService);

        // Glyph: body + terminal nub + fill, 26x12 at font 14, scaling with the font.
        var body = new Rectangle
        {
            Stroke = Brushes.White, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2,
            [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.GlyphWidth)),
            [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.GlyphHeight)),
        };
        var fill = new Rectangle
        {
            RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2.5, 0, 0, 0),
            [!Shape.FillProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.FillBrush)),
            [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.FillWidth)),
            [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.FillHeight)),
        };
        var nub = new Rectangle
        {
            Fill = Brushes.White, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 0, 0),
            [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.NubWidth)),
            [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.NubHeight)),
        };
        var bolt = new TextBlock
        {
            Text = "\u26A1", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.BoltSize)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.IsCharging)),
        };
        var glyph = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Panel { Children = { body, fill, bolt } }, nub },
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.HasBattery)),
        };

        var percent = new TextBlock
        {
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.PercentText)),
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.FontSize)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.ShowPercent)),
        };
        var status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.StatusText)),
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.StatusFontSize)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(BatteryWidgetViewModel.HasStatus)),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { glyph, percent } };
        return new StackPanel
        {
            DataContext = vm, Margin = new Thickness(8, 4), IsHitTestVisible = false, Spacing = 1,
            Children = { row, status },
        };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;

        CheckBox Toggle(string key, string text, bool def)
        {
            var cb = new CheckBox { Content = loc.Text(text), IsChecked = definition.GetSettingBool(key, def), Foreground = Brushes.LightGray };
            cb.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, key, (cb.IsChecked == true).ToString()); onChanged(); };
            return cb;
        }
        var size = new Slider { Minimum = 10, Maximum = 48, Value = definition.GetSettingInt(BatteryWidgetSettings.FontSize, 14) };
        size.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, BatteryWidgetSettings.FontSize, ((int)Math.Round(size.Value)).ToString()); onChanged(); };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = loc.Text("settings.widget.battery.helper"), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#888888")), TextWrapping = TextWrapping.Wrap },
                Toggle(BatteryWidgetSettings.ShowPercent, "settings.widget.battery.showPercent", true),
                Toggle(BatteryWidgetSettings.ShowTime, "settings.widget.battery.showTime", true),
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.fontSize.title"), null, size),
            }
        };
    }
}

public sealed class BatteryWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly IBatteryGateway _gateway;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _showTime = true;

    private bool _hasBattery = true;
    public bool HasBattery { get => _hasBattery; set => SetProperty(ref _hasBattery, value); }
    private bool _isCharging;
    public bool IsCharging { get => _isCharging; set => SetProperty(ref _isCharging, value); }
    private bool _showPercent = true;
    public bool ShowPercent { get => _showPercent; set => SetProperty(ref _showPercent, value); }
    private string _percentText = "";
    public string PercentText { get => _percentText; set => SetProperty(ref _percentText, value); }
    private string _statusText = "";
    public string StatusText { get => _statusText; set { if (SetProperty(ref _statusText, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => _statusText.Length > 0;
    private IBrush _fillBrush = Brushes.LimeGreen;
    public IBrush FillBrush { get => _fillBrush; set => SetProperty(ref _fillBrush, value); }
    private double _fillWidth;
    public double FillWidth { get => _fillWidth; set => SetProperty(ref _fillWidth, value); }

    private double _fontSize = 14;
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (!SetProperty(ref _fontSize, value)) return;
            foreach (var n in new[] { nameof(GlyphWidth), nameof(GlyphHeight), nameof(FillHeight), nameof(NubWidth), nameof(NubHeight), nameof(BoltSize), nameof(StatusFontSize) })
                OnPropertyChanged(n);
        }
    }
    public double GlyphWidth => Math.Round(FontSize * 1.9);
    public double GlyphHeight => Math.Round(FontSize * 0.9);
    public double FillHeight => Math.Max(2, GlyphHeight - 5);
    public double NubWidth => Math.Max(2, Math.Round(FontSize * 0.15));
    public double NubHeight => Math.Max(3, Math.Round(GlyphHeight * 0.45));
    public double BoltSize => Math.Max(8, FontSize * 0.7);
    public double StatusFontSize => Math.Max(9, Math.Round(FontSize * 0.75));

    public BatteryWidgetViewModel(WidgetDefinition definition, IBatteryGateway gateway, LocalizationService loc)
    {
        _definition = definition; _gateway = gateway; _loc = loc;
        _timer.Tick += (_, _) => Tick();
        Refresh();
        _timer.Start();
    }

    public override void Refresh()
    {
        ShowPercent = _definition.GetSettingBool(BatteryWidgetSettings.ShowPercent, true);
        _showTime = _definition.GetSettingBool(BatteryWidgetSettings.ShowTime, true);
        FontSize = _definition.GetSettingInt(BatteryWidgetSettings.FontSize, 14);
        Tick();
    }

    private void Tick()
    {
        var s = _gateway.Read();
        HasBattery = s.HasBattery;
        IsCharging = s.IsCharging;
        if (!s.HasBattery)
        {
            PercentText = _loc.Text("widget.battery.none");
            StatusText = "";
            return;
        }
        PercentText = s.Percent + "%";
        StatusText = BatteryFormats.StatusLine(s, _showTime, k => _loc.Text(k));
        double inner = GlyphWidth - 5;
        FillWidth = Math.Max(0, Math.Round(inner * s.Percent / 100.0));
        FillBrush = s.Percent <= 15 && !s.IsCharging ? Brushes.OrangeRed
            : s.Percent <= 30 && !s.IsCharging ? Brushes.Gold
            : Brushes.LimeGreen;
    }

    public override void Shutdown() => _timer.Stop();
}
