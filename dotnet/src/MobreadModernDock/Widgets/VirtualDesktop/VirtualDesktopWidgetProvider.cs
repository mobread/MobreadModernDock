using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.VirtualDesktop;

/// <summary>
/// Virtual desktops widget: one pill per desktop, the current one filled.
/// Clicking a pill switches to it. Names come from Task View; unnamed
/// desktops show their number. Polls the registry once a second (the
/// shell writes it synchronously on every change).
/// </summary>
public sealed class VirtualDesktopWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.VirtualDesktop;
    public string DisplayNameKey => "widget.type.vdesktop";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [VirtualDesktopWidgetSettings.ShowNames] = "true",
        [VirtualDesktopWidgetSettings.FontSize] = "13",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new VirtualDesktopWidgetViewModel(definition, services.VirtualDesktopGateway);
        var items = new ItemsControl
        {
            DataContext = vm,
            ItemsSource = vm.Pills,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }),
            ItemTemplate = new FuncDataTemplate<DesktopPill>((pill, _) =>
            {
                var text = new TextBlock
                {
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(DesktopPill.Label)),
                    [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(VirtualDesktopWidgetViewModel.FontSize)) { Source = vm },
                    [!TextBlock.FontWeightProperty] = new Avalonia.Data.Binding(nameof(DesktopPill.Weight)),
                };
                var btn = new Button
                {
                    Content = text, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(10, 3), MinWidth = 28, HorizontalContentAlignment = HorizontalAlignment.Center,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    [!TemplatedControl.BackgroundProperty] = new Avalonia.Data.Binding(nameof(DesktopPill.Background)),
                };
                btn.Click += (_, _) => vm.Switch(pill.Index);
                return btn;
            }, supportsRecycling: false),
            Margin = new Thickness(6, 3),
        };
        return items;
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var names = new CheckBox { Content = loc.Text("settings.widget.vdesktop.showNames"), IsChecked = definition.GetSettingBool(VirtualDesktopWidgetSettings.ShowNames, true), Foreground = Brushes.LightGray };
        names.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, VirtualDesktopWidgetSettings.ShowNames, (names.IsChecked == true).ToString()); onChanged(); };
        var size = new Slider { Minimum = 9, Maximum = 32, Value = definition.GetSettingInt(VirtualDesktopWidgetSettings.FontSize, 13) };
        size.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, VirtualDesktopWidgetSettings.FontSize, ((int)Math.Round(size.Value)).ToString()); onChanged(); };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = loc.Text("settings.widget.vdesktop.helper"), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#888888")), TextWrapping = TextWrapping.Wrap },
                names,
                Text.TextWidgetProvider.Section(loc.Text("settings.widget.fontSize.title"), null, size),
            }
        };
    }
}

public sealed class DesktopPill : ViewModels.ViewModelBase
{
    public int Index { get; }
    public DesktopPill(int index) => Index = index;

    private string _label = "";
    public string Label { get => _label; set => SetProperty(ref _label, value); }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (SetProperty(ref _isCurrent, value)) { OnPropertyChanged(nameof(Background)); OnPropertyChanged(nameof(Weight)); } }
    }
    public IBrush Background => _isCurrent ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
    public FontWeight Weight => _isCurrent ? FontWeight.SemiBold : FontWeight.Normal;
}

public sealed class VirtualDesktopWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly IVirtualDesktopGateway _gateway;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _showNames = true;

    public ObservableCollection<DesktopPill> Pills { get; } = new();

    private double _fontSize = 13;
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

    public VirtualDesktopWidgetViewModel(WidgetDefinition definition, IVirtualDesktopGateway gateway)
    {
        _definition = definition; _gateway = gateway;
        _timer.Tick += (_, _) => Tick();
        Refresh();
        _timer.Start();
    }

    public override void Refresh()
    {
        _showNames = _definition.GetSettingBool(VirtualDesktopWidgetSettings.ShowNames, true);
        FontSize = _definition.GetSettingInt(VirtualDesktopWidgetSettings.FontSize, 13);
        Tick();
    }

    private void Tick()
    {
        var snap = _gateway.Read();
        // Keep pill instances stable so a click mid-refresh still targets the right one.
        while (Pills.Count > snap.Desktops.Count) Pills.RemoveAt(Pills.Count - 1);
        while (Pills.Count < snap.Desktops.Count) Pills.Add(new DesktopPill(Pills.Count));
        for (int i = 0; i < snap.Desktops.Count; i++)
        {
            Pills[i].Label = VirtualDesktopFormats.Label(snap.Desktops[i], i, _showNames);
            Pills[i].IsCurrent = i == snap.CurrentIndex;
        }
    }

    public void Switch(int index)
    {
        _gateway.SwitchTo(index);
        // Reflect immediately; the poll confirms a moment later.
        for (int i = 0; i < Pills.Count; i++) Pills[i].IsCurrent = i == index;
    }

    public override void Shutdown() => _timer.Stop();
}
