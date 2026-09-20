using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Models;

namespace CedroModernDock.Widgets.Clock;

/// <summary>
/// Clock widget: local time in a choice of layouts (24h/12h, with or without
/// seconds, weekday/date on a second line) or any .NET format string.
/// </summary>
public sealed class ClockWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Clock;
    public string DisplayNameKey => "widget.type.clock";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [ClockWidgetSettings.Preset] = "time24-date",
        [ClockWidgetSettings.FontSize] = "20",
        [ClockWidgetSettings.TwoLines] = "true",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new ClockWidgetViewModel(definition);
        var time = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ClockWidgetViewModel.TimeText)),
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(ClockWidgetViewModel.FontSize)),
        };
        var date = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ClockWidgetViewModel.DateText)),
            [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(ClockWidgetViewModel.DateFontSize)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(ClockWidgetViewModel.HasDate)),
        };
        return new StackPanel
        {
            DataContext = vm,
            Spacing = 0,
            Margin = new Thickness(6, 2),
            IsHitTestVisible = false,
            Children = { time, date },
        };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var now = DateTime.Now;

        // Preset picker shows a live sample of each layout, not the key.
        var presetCombo = new ComboBox { MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var entries = ClockFormats.Presets
            .Select(p => new PresetEntry(p.Key, ClockFormats.Format(now, p.TimeFormat) + (p.DateFormat != null ? "   " + ClockFormats.Format(now, p.DateFormat) : "")))
            .ToList();
        entries.Add(new PresetEntry(ClockFormats.Custom, loc.Text("settings.widget.clock.customFormat")));
        foreach (var e in entries) presetCombo.Items.Add(e);
        string currentPreset = definition.GetSetting(ClockWidgetSettings.Preset, "time24-date");
        presetCombo.SelectedItem = entries.FirstOrDefault(e => e.Key == currentPreset) ?? entries[0];

        var customBox = new TextBox
        {
            Text = definition.GetSetting(ClockWidgetSettings.CustomFormat, "HH:mm:ss  dddd, d MMMM yyyy"),
            Width = 320, HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = currentPreset == ClockFormats.Custom,
            Watermark = "HH:mm:ss  dddd, d MMMM yyyy",
        };
        var preview = new TextBlock { Foreground = Brushes.White, FontSize = 16 };
        void UpdatePreview()
        {
            var (t, d) = ClockFormats.Resolve(
                (presetCombo.SelectedItem as PresetEntry)?.Key ?? "time24-date", customBox.Text);
            preview.Text = ClockFormats.Format(DateTime.Now, t) + (d != null ? "  ·  " + ClockFormats.Format(DateTime.Now, d) : "");
        }
        UpdatePreview();

        presetCombo.SelectionChanged += (_, _) =>
        {
            if (presetCombo.SelectedItem is not PresetEntry e) return;
            widgets.UpdateSetting(definition.Id, ClockWidgetSettings.Preset, e.Key);
            customBox.IsEnabled = e.Key == ClockFormats.Custom;
            UpdatePreview();
            onChanged();
        };
        customBox.TextChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, ClockWidgetSettings.CustomFormat, customBox.Text ?? "");
            UpdatePreview();
            onChanged();
        };

        var twoLines = new CheckBox
        {
            Content = loc.Text("settings.widget.clock.twoLines"),
            IsChecked = definition.GetSettingBool(ClockWidgetSettings.TwoLines, true),
            Foreground = Brushes.LightGray,
        };
        twoLines.IsCheckedChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, ClockWidgetSettings.TwoLines, (twoLines.IsChecked == true).ToString());
            onChanged();
        };

        var sizeSlider = new Slider { Minimum = 10, Maximum = 72, Value = definition.GetSettingInt(ClockWidgetSettings.FontSize, 20) };
        sizeSlider.ValueChanged += (_, _) =>
        {
            widgets.UpdateSetting(definition.Id, ClockWidgetSettings.FontSize, ((int)Math.Round(sizeSlider.Value)).ToString());
            onChanged();
        };

        var customHelp = new TextBlock
        {
            Text = loc.Text("settings.widget.clock.customHelper"),
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#888888")), TextWrapping = TextWrapping.Wrap,
        };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.clock.layout"), null, presetCombo),
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.clock.customFormat"), null,
                    new StackPanel { Spacing = 4, Children = { customBox, customHelp } }),
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.preview.title"), null, preview),
                twoLines,
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.fontSize.title"), null, sizeSlider),
            }
        };
    }

    private sealed record PresetEntry(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}

public sealed class ClockWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly DispatcherTimer _timer = new();
    private string _timeFormat = "HH:mm";
    private string? _dateFormat;
    private bool _twoLines = true;

    private string _timeText = "";
    public string TimeText { get => _timeText; set => SetProperty(ref _timeText, value); }

    private string _dateText = "";
    public string DateText { get => _dateText; set => SetProperty(ref _dateText, value); }

    private bool _hasDate;
    public bool HasDate { get => _hasDate; set => SetProperty(ref _hasDate, value); }

    private double _fontSize = 20;
    public double FontSize
    {
        get => _fontSize;
        set { if (SetProperty(ref _fontSize, value)) OnPropertyChanged(nameof(DateFontSize)); }
    }

    /// <summary>Date line is ~55% of the time line, never below 9pt.</summary>
    public double DateFontSize => Math.Max(9, Math.Round(FontSize * 0.55));

    public ClockWidgetViewModel(WidgetDefinition definition)
    {
        _definition = definition;
        _timer.Tick += (_, _) => Tick();
        Refresh();
    }

    public override void Refresh()
    {
        string preset = _definition.GetSetting(ClockWidgetSettings.Preset, "time24-date");
        (_timeFormat, _dateFormat) = ClockFormats.Resolve(preset, _definition.GetSetting(ClockWidgetSettings.CustomFormat));
        _twoLines = _definition.GetSettingBool(ClockWidgetSettings.TwoLines, true);
        FontSize = _definition.GetSettingInt(ClockWidgetSettings.FontSize, 20);

        // Tick once a second only when seconds are shown; otherwise align to
        // the next minute and tick every minute.
        bool seconds = ClockFormats.HasSeconds(_timeFormat) || (_dateFormat != null && ClockFormats.HasSeconds(_dateFormat));
        _timer.Stop();
        _timer.Interval = seconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1); // re-aligned in Tick
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        if (_dateFormat != null && _twoLines)
        {
            TimeText = ClockFormats.Format(now, _timeFormat);
            DateText = ClockFormats.Format(now, _dateFormat);
            HasDate = true;
        }
        else if (_dateFormat != null)
        {
            TimeText = ClockFormats.Format(now, _timeFormat) + "  " + ClockFormats.Format(now, _dateFormat);
            HasDate = false;
        }
        else
        {
            TimeText = ClockFormats.Format(now, _timeFormat);
            HasDate = false;
        }

        // Fire exactly on the next boundary so the display never lags.
        bool seconds = ClockFormats.HasSeconds(_timeFormat) || (_dateFormat != null && ClockFormats.HasSeconds(_dateFormat));
        var next = seconds
            ? now.AddSeconds(1).AddMilliseconds(-now.Millisecond)
            : now.AddMinutes(1).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, (next - now).TotalMilliseconds));
    }

    public override void Shutdown() => _timer.Stop();
}
