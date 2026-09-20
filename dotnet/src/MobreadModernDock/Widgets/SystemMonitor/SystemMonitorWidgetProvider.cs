using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.SystemMonitor;

public static class SystemMonitorSettings
{
    public const string ShowCpu = "showCpu";
    public const string ShowRam = "showRam";
    public const string ShowGpu = "showGpu";
    public const string ShowNet = "showNet";
    public const string Compact = "compact";     // bars only, no numbers
    public const string IntervalMs = "intervalMs";
}

/// <summary>CPU / RAM / GPU / network as labelled bars, refreshed on a timer.</summary>
public sealed class SystemMonitorWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.SystemMonitor;
    public string DisplayNameKey => "widget.type.sysmon";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [SystemMonitorSettings.ShowCpu] = "true",
        [SystemMonitorSettings.ShowRam] = "true",
        [SystemMonitorSettings.ShowGpu] = "true",
        [SystemMonitorSettings.ShowNet] = "true",
        [SystemMonitorSettings.Compact] = "false",
        [SystemMonitorSettings.IntervalMs] = "1000",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new SystemMonitorViewModel(definition, services);
        var list = new ItemsControl
        {
            DataContext = vm,
            ItemsSource = vm.Rows,
            Margin = new Thickness(2),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MetricRow>((row, _) => BuildRow(row), supportsRecycling: true),
        };
        return list;
    }

    private static Control BuildRow(MetricRow row)
    {
        var label = new TextBlock
        {
            Text = row.Label, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 11, Width = 30, VerticalAlignment = VerticalAlignment.Center,
        };
        var track = new Border
        {
            Width = 90, Height = 8, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
                [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(MetricRow.BarWidth)),
                [!Border.BackgroundProperty] = new Avalonia.Data.Binding(nameof(MetricRow.BarBrush)),
            },
        };
        var value = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 11, MinWidth = 62, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(MetricRow.ValueText)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(MetricRow.ShowValue)),
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2, 2),
            Children = { label, track, value },
        };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var panel = new StackPanel { Spacing = 8 };

        foreach (var (key, textKey) in new[]
        {
            (SystemMonitorSettings.ShowCpu, "settings.widget.sysmon.cpu"),
            (SystemMonitorSettings.ShowRam, "settings.widget.sysmon.ram"),
            (SystemMonitorSettings.ShowGpu, "settings.widget.sysmon.gpu"),
            (SystemMonitorSettings.ShowNet, "settings.widget.sysmon.net"),
            (SystemMonitorSettings.Compact, "settings.widget.sysmon.compact"),
        })
        {
            var cb = new CheckBox { Content = loc.Text(textKey), IsChecked = definition.GetSettingBool(key, key != SystemMonitorSettings.Compact), Foreground = Brushes.LightGray };
            string k = key;
            cb.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, k, (cb.IsChecked == true).ToString()); onChanged(); };
            panel.Children.Add(cb);
        }

        var interval = new Slider { Minimum = 500, Maximum = 5000, TickFrequency = 250, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(SystemMonitorSettings.IntervalMs, 1000) };
        var intervalLabel = new TextBlock { Foreground = Brushes.White, FontSize = 12, Text = $"{(int)interval.Value} ms" };
        interval.ValueChanged += (_, _) =>
        {
            int ms = (int)Math.Round(interval.Value);
            intervalLabel.Text = $"{ms} ms";
            widgets.UpdateSetting(definition.Id, SystemMonitorSettings.IntervalMs, ms.ToString());
            onChanged();
        };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.sysmon.interval"), null,
            new StackPanel { Spacing = 4, Children = { interval, intervalLabel } }));
        return panel;
    }
}

public sealed class MetricRow : ViewModels.ViewModelBase
{
    public string Label { get; }
    public MetricRow(string label) { Label = label; }

    private double _barWidth;
    public double BarWidth { get => _barWidth; set => SetProperty(ref _barWidth, value); }

    private IBrush _barBrush = Brushes.DeepSkyBlue;
    public IBrush BarBrush { get => _barBrush; set => SetProperty(ref _barBrush, value); }

    private string _valueText = "";
    public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value); }

    private bool _showValue = true;
    public bool ShowValue { get => _showValue; set => SetProperty(ref _showValue, value); }

    /// <summary>Set the bar from a 0..100 percentage; colour shifts green→amber→red with load.</summary>
    public void SetPercent(double pct, string text, bool showValue)
    {
        pct = Math.Clamp(pct, 0, 100);
        BarWidth = 90 * pct / 100.0;
        BarBrush = pct < 60 ? new SolidColorBrush(Color.Parse("#4CAF50"))
                 : pct < 85 ? new SolidColorBrush(Color.Parse("#FFB300"))
                            : new SolidColorBrush(Color.Parse("#E53935"));
        ValueText = text;
        ShowValue = showValue;
    }
}

public sealed class SystemMonitorViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;
    private int _intervalMs = 1000;
    private bool _compact;
    private bool _cpu, _ram, _gpu, _net;

    public ObservableCollection<MetricRow> Rows { get; } = new();
    private readonly MetricRow _cpuRow = new("CPU"), _ramRow = new("RAM"), _gpuRow = new("GPU"), _netRow = new("NET");

    // Network bar is relative to a sliding peak so it stays meaningful for
    // both a 10 Mbit link and a 2.5 Gbit one.
    private double _netPeak = 1024 * 1024; // 1 MB/s floor

    public SystemMonitorViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition;
        _services = services;
        Refresh();
        Start();
    }

    public override void Refresh()
    {
        _cpu = _definition.GetSettingBool(SystemMonitorSettings.ShowCpu, true);
        _ram = _definition.GetSettingBool(SystemMonitorSettings.ShowRam, true);
        _gpu = _definition.GetSettingBool(SystemMonitorSettings.ShowGpu, true);
        _net = _definition.GetSettingBool(SystemMonitorSettings.ShowNet, true);
        _compact = _definition.GetSettingBool(SystemMonitorSettings.Compact, false);
        _intervalMs = Math.Clamp(_definition.GetSettingInt(SystemMonitorSettings.IntervalMs, 1000), 500, 10000);

        Rows.Clear();
        if (_cpu) Rows.Add(_cpuRow);
        if (_ram) Rows.Add(_ramRow);
        if (_gpu) Rows.Add(_gpuRow);
        if (_net) Rows.Add(_netRow);
    }

    private void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var s = _services.SystemStatsGateway.Sample();
                    Dispatcher.UIThread.Post(() => Apply(s));
                }
                catch { }
                try { await Task.Delay(_intervalMs, token); } catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void Apply(SystemStats s)
    {
        _cpuRow.SetPercent(s.CpuPercent, $"{s.CpuPercent:0}%", !_compact);
        _ramRow.SetPercent(s.RamPercent, $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB", !_compact);
        if (s.GpuPercent is double g) _gpuRow.SetPercent(g, $"{g:0}%", !_compact);
        else _gpuRow.SetPercent(0, "n/a", !_compact);

        double total = s.NetDownBytesPerSec + s.NetUpBytesPerSec;
        _netPeak = Math.Max(_netPeak * 0.98, Math.Max(total, 1024 * 1024));
        _netRow.SetPercent(total / _netPeak * 100.0, $"↓{Rate(s.NetDownBytesPerSec)} ↑{Rate(s.NetUpBytesPerSec)}", !_compact);
    }

    private static string Rate(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / 1024 / 1024:0.0}M";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0}K";
        return $"{bytesPerSec:0}B";
    }

    public override void Shutdown()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
