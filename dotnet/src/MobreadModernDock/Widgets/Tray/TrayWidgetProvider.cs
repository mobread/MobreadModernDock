using System;
using System.Collections.Generic;
using Avalonia.Controls;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.Tray;

/// <summary>
/// System tray widget: mirrors the Windows notification-area icons in a
/// floating window. Left-click activates, right-click opens the icon's
/// context menu.
/// </summary>
public sealed class TrayWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Tray;
    public string DisplayNameKey => "widget.type.tray";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [TrayWidgetSettings.IconSize] = "20",
        [TrayWidgetSettings.Spacing] = "6",
        [TrayWidgetSettings.Vertical] = "false",
        [TrayWidgetSettings.ShowSystemIcons] = "false",
        [TrayWidgetSettings.Lines] = "1",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        return new TrayWidgetView { DataContext = new TrayWidgetViewModel(definition, services) };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;

        var sizeSlider = new Slider { Minimum = 12, Maximum = 48, Value = definition.GetSettingInt(TrayWidgetSettings.IconSize, 20) };
        sizeSlider.ValueChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TrayWidgetSettings.IconSize, ((int)Math.Round(sizeSlider.Value)).ToString());
            onChanged();
        };

        var spacingSlider = new Slider { Minimum = 0, Maximum = 24, Value = definition.GetSettingInt(TrayWidgetSettings.Spacing, 6) };
        spacingSlider.ValueChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TrayWidgetSettings.Spacing, ((int)Math.Round(spacingSlider.Value)).ToString());
            onChanged();
        };

        var vertical = new CheckBox
        {
            Content = loc.Text("settings.widget.tray.vertical"),
            IsChecked = definition.GetSettingBool(TrayWidgetSettings.Vertical, false),
            Foreground = Avalonia.Media.Brushes.LightGray,
        };
        vertical.IsCheckedChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TrayWidgetSettings.Vertical, (vertical.IsChecked == true).ToString());
            onChanged();
        };

        var showSystem = new CheckBox
        {
            Content = loc.Text("settings.widget.tray.showSystemIcons"),
            IsChecked = definition.GetSettingBool(TrayWidgetSettings.ShowSystemIcons, false),
            Foreground = Avalonia.Media.Brushes.LightGray,
        };
        showSystem.IsCheckedChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TrayWidgetSettings.ShowSystemIcons, (showSystem.IsChecked == true).ToString());
            onChanged();
        };

        var linesSlider = new Slider { Minimum = 1, Maximum = 6, TickFrequency = 1, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(TrayWidgetSettings.Lines, 1) };
        var linesLabel = new TextBlock { Foreground = Avalonia.Media.Brushes.White, FontSize = 12 };
        void UpdateLinesLabel() => linesLabel.Text = string.Format(
            loc.Text(vertical.IsChecked == true ? "settings.widget.tray.lines.columns" : "settings.widget.tray.lines.rows"), (int)linesSlider.Value);
        UpdateLinesLabel();
        linesSlider.ValueChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TrayWidgetSettings.Lines, ((int)Math.Round(linesSlider.Value)).ToString());
            UpdateLinesLabel();
            onChanged();
        };
        vertical.IsCheckedChanged += (_, _) => UpdateLinesLabel();

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = loc.Text("settings.widget.tray.helper"), FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.tray.iconSize"), null, sizeSlider),
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.tray.spacing"), null, spacingSlider),
                vertical,
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.tray.lines"), loc.Text("settings.widget.tray.lines.helper"),
                    new StackPanel { Spacing = 4, Children = { linesSlider, linesLabel } }),
                showSystem,
            }
        };
    }
}
