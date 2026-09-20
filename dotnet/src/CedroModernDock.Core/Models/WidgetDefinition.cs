namespace CedroModernDock.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// One persisted floating widget. <see cref="Type"/> selects the provider
/// (e.g. "text", "tray"); <see cref="Settings"/> carries provider-specific
/// options as plain strings so new widget types need no model changes.
/// </summary>
public class WidgetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("positionX")]
    public double PositionX { get; set; } = double.NaN;

    [JsonPropertyName("positionY")]
    public double PositionY { get; set; } = double.NaN;

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new();

    [JsonIgnore]
    public bool HasPosition => !double.IsNaN(PositionX) && !double.IsNaN(PositionY);

    public string GetSetting(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var v) ? v : fallback;

    public int GetSettingInt(string key, int fallback) =>
        Settings.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    public bool GetSettingBool(string key, bool fallback) =>
        Settings.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;

    public void SetSetting(string key, string value) => Settings[key] = value;
}
