using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.Text;

/// <summary>
/// Text widget: a single line of user-defined text. The template supports
/// <c>{host}</c> and <c>{user}</c> placeholders.
/// </summary>
public sealed class TextWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Text;
    public string DisplayNameKey => "widget.type.text";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [TextWidgetSettings.Template] = "{host}",
        [TextWidgetSettings.FontSize] = "14",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new TextWidgetViewModel(definition);
        return new TextBlock
        {
            DataContext = vm,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(4, 2),
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(TextWidgetViewModel.Text)),
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(TextWidgetViewModel.FontSize)),
        };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;

        var textBox = new TextBox
        {
            Text = definition.GetSetting(TextWidgetSettings.Template, "{host}"),
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var preview = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = definition.GetSettingInt(TextWidgetSettings.FontSize, 14),
            Text = WidgetService.ResolveText(textBox.Text),
        };
        var slider = new Slider
        {
            Minimum = 8, Maximum = 72,
            Value = definition.GetSettingInt(TextWidgetSettings.FontSize, 14),
        };

        textBox.TextChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, TextWidgetSettings.Template, textBox.Text ?? "");
            preview.Text = WidgetService.ResolveText(textBox.Text);
            onChanged();
        };
        slider.ValueChanged += (_, _) =>
        {
            int size = (int)Math.Round(slider.Value);
            widgets.UpdateSetting(definition.Id, TextWidgetSettings.FontSize, size.ToString());
            preview.FontSize = size;
            onChanged();
        };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Section(loc.Text("settings.widget.text.title"), loc.Text("settings.widget.text.helper"), textBox),
                Section(loc.Text("settings.widget.preview.title"), null, preview),
                Section(loc.Text("settings.widget.fontSize.title"), null, slider),
            }
        };
    }

    internal static StackPanel Section(string title, string? helper, Control body)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) });
        if (!string.IsNullOrEmpty(helper))
            panel.Children.Add(new TextBlock { Text = helper, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#888888")), TextWrapping = TextWrapping.Wrap });
        body.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(body);
        return panel;
    }
}

public sealed class TextWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;

    private string _text = "";
    public string Text { get => _text; set => SetProperty(ref _text, value); }

    private double _fontSize = 14;
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

    public TextWidgetViewModel(WidgetDefinition definition)
    {
        _definition = definition;
        Refresh();
    }

    public override void Refresh()
    {
        Text = WidgetService.ResolveText(_definition.GetSetting(TextWidgetSettings.Template, "{host}"));
        FontSize = _definition.GetSettingInt(TextWidgetSettings.FontSize, 14);
    }
}
