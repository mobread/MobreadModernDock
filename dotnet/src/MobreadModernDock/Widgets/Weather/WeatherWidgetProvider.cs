using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

namespace MobreadModernDock.Widgets.Weather;

public static class WeatherWidgetSettings
{
    public const string Latitude = "lat";
    public const string Longitude = "lon";
    public const string LocationName = "location";
    public const string Fahrenheit = "fahrenheit";
    public const string ShowForecast = "showForecast";
    public const string ShowDetails = "showDetails";   // humidity + wind line
    public const string RefreshMinutes = "refreshMinutes";
}

/// <summary>Current conditions + optional 5-day strip, via Open-Meteo.</summary>
public sealed class WeatherWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Weather;
    public string DisplayNameKey => "widget.type.weather";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [WeatherWidgetSettings.Latitude] = "",
        [WeatherWidgetSettings.Longitude] = "",
        [WeatherWidgetSettings.LocationName] = "",
        [WeatherWidgetSettings.Fahrenheit] = RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "BS" or "BZ" or "KY" or "PW" ? "true" : "false",
        [WeatherWidgetSettings.ShowForecast] = "true",
        [WeatherWidgetSettings.ShowDetails] = "true",
        [WeatherWidgetSettings.RefreshMinutes] = "15",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new WeatherWidgetViewModel(definition, services);
        var dim = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF));

        var glyph = new TextBlock { FontSize = 30, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Glyph)) };
        var temp = new TextBlock { FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Temperature)) };
        var location = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Location)) };
        var condition = new TextBlock { FontSize = 11, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Condition)) };
        var details = new TextBlock { FontSize = 11, Foreground = dim,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Details)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.ShowDetails)) };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                glyph, temp,
                new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, Children = { location, condition, details } },
            }
        };

        var forecast = new ItemsControl
        {
            Margin = new Thickness(0, 6, 0, 0),
            [!ItemsControl.ItemsSourceProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.Forecast)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(WeatherWidgetViewModel.ShowForecast)),
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ForecastDayRow>((row, _) => new StackPanel
            {
                Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 36,
                Children =
                {
                    new TextBlock { Text = row.Day, FontSize = 10, Foreground = dim, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = row.Glyph, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = row.HighLow, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                }
            }, supportsRecycling: true),
        };

        return new StackPanel { DataContext = vm, Margin = new Thickness(8, 4), Children = { header, forecast } };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var panel = new StackPanel { Spacing = 8 };

        // Location search
        var search = new TextBox { Watermark = loc.Text("settings.widget.weather.searchHint"), Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var results = new ListBox { Width = 320, MaxHeight = 160, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        var current = new TextBlock { Foreground = Brushes.White, FontSize = 12, Text = CurrentLocationText(definition, loc) };
        CancellationTokenSource? searchCts = null;
        search.TextChanged += async (_, _) =>
        {
            searchCts?.Cancel();
            var cts = searchCts = new CancellationTokenSource();
            string q = search.Text ?? "";
            if (q.Trim().Length < 2) { results.IsVisible = false; return; }
            try
            {
                await Task.Delay(350, cts.Token);
                var found = await services.WeatherGateway.SearchLocationAsync(q, cts.Token);
                if (cts.IsCancellationRequested) return;
                results.ItemsSource = found.Select(g => new ListBoxItem { Content = g.Name, Tag = g, Foreground = Brushes.White }).ToList();
                results.IsVisible = found.Count > 0;
            }
            catch (OperationCanceledException) { }
            catch { results.IsVisible = false; }
        };
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is ListBoxItem { Tag: GeoLocation g })
            {
                widgets.UpdateSetting(definition.Id, WeatherWidgetSettings.Latitude, g.Latitude.ToString(CultureInfo.InvariantCulture));
                widgets.UpdateSetting(definition.Id, WeatherWidgetSettings.Longitude, g.Longitude.ToString(CultureInfo.InvariantCulture));
                widgets.UpdateSetting(definition.Id, WeatherWidgetSettings.LocationName, g.Name);
                current.Text = CurrentLocationText(definition, loc);
                results.IsVisible = false;
                search.Text = "";
                onChanged();
            }
        };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.weather.location"), loc.Text("settings.widget.weather.locationHelper"),
            new StackPanel { Spacing = 4, Children = { current, search, results } }));

        foreach (var (key, textKey, def) in new[]
        {
            (WeatherWidgetSettings.Fahrenheit, "settings.widget.weather.fahrenheit", false),
            (WeatherWidgetSettings.ShowForecast, "settings.widget.weather.forecast", true),
            (WeatherWidgetSettings.ShowDetails, "settings.widget.weather.details", true),
        })
        {
            var cb = new CheckBox { Content = loc.Text(textKey), IsChecked = definition.GetSettingBool(key, def), Foreground = Brushes.LightGray };
            string k = key;
            cb.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, k, (cb.IsChecked == true).ToString()); onChanged(); };
            panel.Children.Add(cb);
        }

        var interval = new Slider { Minimum = 5, Maximum = 60, TickFrequency = 5, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(WeatherWidgetSettings.RefreshMinutes, 15) };
        var intervalLabel = new TextBlock { Foreground = Brushes.White, FontSize = 12, Text = $"{(int)interval.Value} min" };
        interval.ValueChanged += (_, _) =>
        {
            int m = (int)Math.Round(interval.Value);
            intervalLabel.Text = $"{m} min";
            widgets.UpdateSetting(definition.Id, WeatherWidgetSettings.RefreshMinutes, m.ToString());
            onChanged();
        };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.weather.interval"), null, new StackPanel { Spacing = 4, Children = { interval, intervalLabel } }));
        return panel;
    }

    private static string CurrentLocationText(WidgetDefinition d, LocalizationService loc)
    {
        string name = d.GetSetting(WeatherWidgetSettings.LocationName, "");
        return string.IsNullOrEmpty(name) ? loc.Text("settings.widget.weather.noLocation") : name;
    }
}

public sealed class ForecastDayRow
{
    public string Day { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string HighLow { get; init; } = "";
}

public sealed class WeatherWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;
    private double _lat, _lon;
    private string _locationName = "";
    private bool _fahrenheit;
    private int _refreshMinutes = 15;
    private WeatherReport? _last;

    private string _glyph = "…";
    public string Glyph { get => _glyph; set => SetProperty(ref _glyph, value); }
    private string _temperature = "";
    public string Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    private string _location = "";
    public string Location { get => _location; set => SetProperty(ref _location, value); }
    private string _condition = "";
    public string Condition { get => _condition; set => SetProperty(ref _condition, value); }
    private string _details = "";
    public string Details { get => _details; set => SetProperty(ref _details, value); }
    private bool _showForecast = true, _showDetails = true;
    public bool ShowForecast { get => _showForecast; set => SetProperty(ref _showForecast, value); }
    public bool ShowDetails { get => _showDetails; set => SetProperty(ref _showDetails, value); }
    public ObservableCollection<ForecastDayRow> Forecast { get; } = new();

    public WeatherWidgetViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition;
        _services = services;
        Refresh();
    }

    public override void Refresh()
    {
        var ci = CultureInfo.InvariantCulture;
        double.TryParse(_definition.GetSetting(WeatherWidgetSettings.Latitude, ""), NumberStyles.Float, ci, out double lat);
        double.TryParse(_definition.GetSetting(WeatherWidgetSettings.Longitude, ""), NumberStyles.Float, ci, out double lon);
        string name = _definition.GetSetting(WeatherWidgetSettings.LocationName, "");
        bool f = _definition.GetSettingBool(WeatherWidgetSettings.Fahrenheit, false);
        int mins = Math.Clamp(_definition.GetSettingInt(WeatherWidgetSettings.RefreshMinutes, 15), 5, 120);
        ShowForecast = _definition.GetSettingBool(WeatherWidgetSettings.ShowForecast, true);
        ShowDetails = _definition.GetSettingBool(WeatherWidgetSettings.ShowDetails, true);

        bool locationChanged = lat != _lat || lon != _lon || name != _locationName;
        bool unitChanged = f != _fahrenheit;
        _lat = lat; _lon = lon; _locationName = name; _fahrenheit = f; _refreshMinutes = mins;

        if (string.IsNullOrEmpty(name) || (lat == 0 && lon == 0))
        {
            _cts?.Cancel();
            Glyph = "📍"; Temperature = ""; Location = _services.LocalizationService.Text("widget.weather.setLocation"); Condition = ""; Details = "";
            Forecast.Clear();
            return;
        }
        if (locationChanged || _cts == null) StartPolling();
        else if (unitChanged && _last != null) Apply(_last);
    }

    private void StartPolling()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        double lat = _lat, lon = _lon; string name = _locationName;
        Location = name; Condition = _services.LocalizationService.Text("widget.weather.loading");
        Task.Run(async () =>
        {
            int failures = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var report = await _services.WeatherGateway.FetchAsync(lat, lon, name, token);
                    failures = 0;
                    Dispatcher.UIThread.Post(() => { _last = report; Apply(report); });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    failures++;
                    System.Diagnostics.Debug.WriteLine($"[Weather] {e.Message}");
                    if (_last == null) Dispatcher.UIThread.Post(() => { Glyph = "⚠"; Condition = _services.LocalizationService.Text("widget.weather.error"); });
                }
                // Back off on repeated failures (network down) but never longer than the interval.
                int delayMs = failures == 0 ? _refreshMinutes * 60_000 : Math.Min(_refreshMinutes * 60_000, 30_000 * (1 << Math.Min(failures, 4)));
                try { await Task.Delay(delayMs, token); } catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void Apply(WeatherReport r)
    {
        var (key, glyph) = OpenMeteoWeatherGateway.Describe(r.WeatherCode);
        Glyph = glyph;
        Temperature = Deg(r.TemperatureC);
        Location = r.LocationName;
        Condition = _services.LocalizationService.Text($"weather.code.{key}");
        Details = $"{Deg(r.TodayHighC)} / {Deg(r.TodayLowC)}  ·  {r.HumidityPercent}%  ·  {Wind(r.WindKph)}";

        Forecast.Clear();
        foreach (var d in r.Forecast.Take(5))
        {
            var (_, g) = OpenMeteoWeatherGateway.Describe(d.WeatherCode);
            Forecast.Add(new ForecastDayRow
            {
                Day = d.Date == DateOnly.FromDateTime(DateTime.Today) ? _services.LocalizationService.Text("widget.weather.today") : d.Date.ToString("ddd", CultureInfo.CurrentCulture),
                Glyph = g,
                HighLow = $"{DegShort(d.HighC)}/{DegShort(d.LowC)}",
            });
        }
    }

    private string Deg(double c) => double.IsNaN(c) ? "–" : _fahrenheit ? $"{Math.Round(c * 9 / 5 + 32)}°F" : $"{Math.Round(c)}°C";
    private string DegShort(double c) => double.IsNaN(c) ? "–" : $"{Math.Round(_fahrenheit ? c * 9 / 5 + 32 : c)}°";
    private string Wind(double kph) => _fahrenheit ? $"{Math.Round(kph * 0.621371)} mph" : $"{Math.Round(kph)} km/h";

    public override void Shutdown()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
