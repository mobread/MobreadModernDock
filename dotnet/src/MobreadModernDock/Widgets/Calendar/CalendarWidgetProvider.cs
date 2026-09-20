using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.Calendar;

public static class CalendarWidgetSettings
{
    /// <summary>"system", "monday" or "sunday".</summary>
    public const string FirstDay = "firstDay";
    public const string ShowWeekNumbers = "weekNumbers";
    public const string FontSize = "fontSize";
    public const string ShowNavigation = "navigation";
}

/// <summary>Month view with today highlighted. Navigable; snaps back to the current month on click of the title.</summary>
public sealed class CalendarWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Calendar;
    public string DisplayNameKey => "widget.type.calendar";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [CalendarWidgetSettings.FirstDay] = "system",
        [CalendarWidgetSettings.ShowWeekNumbers] = "false",
        [CalendarWidgetSettings.FontSize] = "12",
        [CalendarWidgetSettings.ShowNavigation] = "true",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new CalendarWidgetViewModel(definition, services);
        var dim = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

        Button Nav(string glyph, Action click) => new()
        {
            Content = glyph, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 0), FontSize = 12, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Command = new Media.Relay(click),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.ShowNavigation)),
        };
        var title = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Center,
            Command = new Media.Relay(vm.GoToToday), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Content = new TextBlock
            {
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.Title)),
                [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.HeaderFontSize)),
            },
        };
        var header = new DockPanel { LastChildFill = true };
        var prev = Nav("‹", vm.PreviousMonth); var next = Nav("›", vm.NextMonth);
        DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        header.Children.Add(prev); header.Children.Add(next); header.Children.Add(title);

        var grid = new ItemsControl
        {
            Margin = new Thickness(0, 4, 0, 0),
            [!ItemsControl.ItemsSourceProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.Cells)),
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new UniformGrid
            {
                [!UniformGrid.ColumnsProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.Columns)),
            }),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CalendarCell>((cell, _) => new Border
            {
                CornerRadius = new CornerRadius(10), Margin = new Thickness(1),
                Background = cell.IsToday ? new SolidColorBrush(Color.Parse("#3A7BD5")) : Brushes.Transparent,
                [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.CellSize)) { Source = vm },
                [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(CalendarWidgetViewModel.CellSize)) { Source = vm },
                Child = new TextBlock
                {
                    Text = cell.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = cell.IsHeader || cell.IsToday ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = cell.IsHeader || cell.IsWeekNumber ? dim : cell.IsOtherMonth ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) : Brushes.White,
                    [!TextBlock.FontSizeProperty] = new Avalonia.Data.Binding(cell.IsHeader || cell.IsWeekNumber ? nameof(CalendarWidgetViewModel.SmallFontSize) : nameof(CalendarWidgetViewModel.FontSize)) { Source = vm },
                },
            }, supportsRecycling: false),
        };

        return new StackPanel { DataContext = vm, Margin = new Thickness(8, 6), Children = { header, grid } };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var panel = new StackPanel { Spacing = 8 };

        var firstDay = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        var options = new[] { ("system", loc.Text("settings.widget.calendar.firstDay.system")), ("monday", loc.Text("settings.widget.calendar.firstDay.monday")), ("sunday", loc.Text("settings.widget.calendar.firstDay.sunday")) };
        foreach (var (_, text) in options) firstDay.Items.Add(new ComboBoxItem { Content = text });
        string cur = definition.GetSetting(CalendarWidgetSettings.FirstDay, "system");
        firstDay.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Item1 == cur));
        firstDay.SelectionChanged += (_, _) => { widgets.UpdateSetting(definition.Id, CalendarWidgetSettings.FirstDay, options[Math.Max(0, firstDay.SelectedIndex)].Item1); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.calendar.firstDay"), null, firstDay));

        foreach (var (key, textKey, def) in new[]
        {
            (CalendarWidgetSettings.ShowWeekNumbers, "settings.widget.calendar.weekNumbers", false),
            (CalendarWidgetSettings.ShowNavigation, "settings.widget.calendar.navigation", true),
        })
        {
            var cb = new CheckBox { Content = loc.Text(textKey), IsChecked = definition.GetSettingBool(key, def), Foreground = Brushes.LightGray };
            string k = key;
            cb.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, k, (cb.IsChecked == true).ToString()); onChanged(); };
            panel.Children.Add(cb);
        }

        var size = new Slider { Minimum = 9, Maximum = 20, TickFrequency = 1, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(CalendarWidgetSettings.FontSize, 12) };
        size.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, CalendarWidgetSettings.FontSize, ((int)size.Value).ToString()); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.fontSize.title"), null, size));
        return panel;
    }
}

public sealed class CalendarCell
{
    public string Text { get; init; } = "";
    public bool IsHeader { get; init; }
    public bool IsWeekNumber { get; init; }
    public bool IsToday { get; init; }
    public bool IsOtherMonth { get; init; }
}

public sealed class CalendarWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly DispatcherTimer _midnight;
    private DateTime _shown;      // first of the displayed month
    private DayOfWeek _firstDay;
    private bool _weekNumbers;

    public ObservableCollection<CalendarCell> Cells { get; } = new();
    private string _title = "";
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    private int _columns = 7;
    public int Columns { get => _columns; set => SetProperty(ref _columns, value); }
    private double _fontSize = 12;
    public double FontSize { get => _fontSize; set { SetProperty(ref _fontSize, value); OnPropertyChanged(nameof(SmallFontSize)); OnPropertyChanged(nameof(HeaderFontSize)); OnPropertyChanged(nameof(CellSize)); } }
    public double SmallFontSize => Math.Max(8, FontSize - 2);
    public double HeaderFontSize => FontSize + 1;
    public double CellSize => Math.Round(FontSize * 2.1);
    private bool _showNavigation = true;
    public bool ShowNavigation { get => _showNavigation; set => SetProperty(ref _showNavigation, value); }

    public CalendarWidgetViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition;
        _shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Refresh();
        // Re-render at midnight so "today" moves; the interval is re-armed on each tick.
        _midnight = new DispatcherTimer { Interval = UntilMidnight() };
        _midnight.Tick += (_, _) => { _midnight.Interval = UntilMidnight(); Build(); };
        _midnight.Start();
    }

    private static TimeSpan UntilMidnight() => DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(1);

    public override void Refresh()
    {
        _firstDay = _definition.GetSetting(CalendarWidgetSettings.FirstDay, "system") switch
        {
            "monday" => DayOfWeek.Monday,
            "sunday" => DayOfWeek.Sunday,
            _ => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
        };
        _weekNumbers = _definition.GetSettingBool(CalendarWidgetSettings.ShowWeekNumbers, false);
        FontSize = _definition.GetSettingInt(CalendarWidgetSettings.FontSize, 12);
        ShowNavigation = _definition.GetSettingBool(CalendarWidgetSettings.ShowNavigation, true);
        Build();
    }

    public void PreviousMonth() { _shown = _shown.AddMonths(-1); Build(); }
    public void NextMonth() { _shown = _shown.AddMonths(1); Build(); }
    public void GoToToday() { _shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Build(); }

    private void Build()
    {
        var culture = CultureInfo.CurrentCulture;
        var today = DateTime.Today;
        Title = _shown.ToString("MMMM yyyy", culture);
        Columns = _weekNumbers ? 8 : 7;

        Cells.Clear();
        if (_weekNumbers) Cells.Add(new CalendarCell { Text = "", IsHeader = true });
        for (int i = 0; i < 7; i++)
        {
            var d = (DayOfWeek)(((int)_firstDay + i) % 7);
            string name = culture.DateTimeFormat.GetAbbreviatedDayName(d);
            Cells.Add(new CalendarCell { Text = name.Length > 2 ? name[..2] : name, IsHeader = true });
        }

        int lead = ((int)_shown.DayOfWeek - (int)_firstDay + 7) % 7;
        var cursor = _shown.AddDays(-lead);
        // Always 6 rows so the widget doesn't resize month to month.
        for (int row = 0; row < 6; row++)
        {
            if (_weekNumbers)
                Cells.Add(new CalendarCell { Text = ISOWeek.GetWeekOfYear(cursor).ToString(), IsWeekNumber = true });
            for (int col = 0; col < 7; col++)
            {
                Cells.Add(new CalendarCell
                {
                    Text = cursor.Day.ToString(),
                    IsToday = cursor.Date == today,
                    IsOtherMonth = cursor.Month != _shown.Month,
                });
                cursor = cursor.AddDays(1);
            }
        }
    }

    public override void Shutdown() => _midnight.Stop();
}
