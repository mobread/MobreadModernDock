namespace MobreadModernDock.Core.Application;

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Weather via Open-Meteo (https://open-meteo.com) — free, no API key, CORS-open.
/// Forecast: api.open-meteo.com; geocoding: geocoding-api.open-meteo.com.
/// </summary>
public sealed class OpenMeteoWeatherGateway : IWeatherGateway
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static OpenMeteoWeatherGateway()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("MobreadModernDock/1.0 (+https://github.com/mobread/MobreadModernDock)");
    }

    public async Task<WeatherReport> FetchAsync(double latitude, double longitude, string locationName, CancellationToken ct)
    {
        string lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        string lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);
        string url = "https://api.open-meteo.com/v1/forecast" +
                     $"?latitude={lat}&longitude={lon}" +
                     "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m" +
                     "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                     "&timezone=auto&forecast_days=5&wind_speed_unit=kmh";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        var dates = daily.GetProperty("time").EnumerateArray().Select(e => DateOnly.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var codes = daily.GetProperty("weather_code").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var highs = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var lows = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var days = new List<WeatherDay>();
        for (int i = 0; i < dates.Length; i++)
            days.Add(new WeatherDay(dates[i], codes[i], highs[i], lows[i]));

        return new WeatherReport(
            locationName,
            cur.GetProperty("temperature_2m").GetDouble(),
            cur.GetProperty("apparent_temperature").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(),
            cur.GetProperty("relative_humidity_2m").GetInt32(),
            cur.GetProperty("wind_speed_10m").GetDouble(),
            days.Count > 0 ? days[0].HighC : double.NaN,
            days.Count > 0 ? days[0].LowC : double.NaN,
            days,
            DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GeoLocation>();
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query.Trim())}&count=8&language=en&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<GeoLocation>();

        var list = new List<GeoLocation>();
        foreach (var r in results.EnumerateArray())
        {
            string name = r.GetProperty("name").GetString() ?? "";
            string admin = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
            string country = r.TryGetProperty("country_code", out var c) ? c.GetString() ?? "" : "";
            string display = string.Join(", ", new[] { name, admin, country }.Where(s => !string.IsNullOrEmpty(s)));
            list.Add(new GeoLocation(display, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
        }
        return list;
    }

    /// <summary>WMO weather-code → i18n key suffix + a glyph. Grouped per the WMO 4677 table Open-Meteo uses.</summary>
    public static (string Key, string Glyph) Describe(int wmoCode) => wmoCode switch
    {
        0 => ("clear", "☀"),
        1 => ("mostlyClear", "🌤"),
        2 => ("partlyCloudy", "⛅"),
        3 => ("overcast", "☁"),
        45 or 48 => ("fog", "🌫"),
        51 or 53 or 55 or 56 or 57 => ("drizzle", "🌦"),
        61 or 63 or 65 or 66 or 67 => ("rain", "🌧"),
        71 or 73 or 75 or 77 => ("snow", "🌨"),
        80 or 81 or 82 => ("showers", "🌧"),
        85 or 86 => ("snowShowers", "🌨"),
        95 => ("thunderstorm", "⛈"),
        96 or 99 => ("thunderstormHail", "⛈"),
        _ => ("unknown", "•"),
    };
}
