namespace MobreadModernDock.Core.Domain;

/// <summary>Current conditions plus a short daily outlook.</summary>
public sealed record WeatherReport(
    string LocationName,
    double TemperatureC,
    double FeelsLikeC,
    int WeatherCode,
    int HumidityPercent,
    double WindKph,
    double TodayHighC,
    double TodayLowC,
    IReadOnlyList<WeatherDay> Forecast,
    DateTimeOffset FetchedAt);

public sealed record WeatherDay(DateOnly Date, int WeatherCode, double HighC, double LowC);

public sealed record GeoLocation(string Name, double Latitude, double Longitude);

/// <summary>Port for weather + geocoding. Implementations are network-backed and may throw.</summary>
public interface IWeatherGateway
{
    Task<WeatherReport> FetchAsync(double latitude, double longitude, string locationName, CancellationToken ct);
    Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct);
}
