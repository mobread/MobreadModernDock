namespace MobreadModernDock.Infrastructure.Windows.Persistence;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// Direct port of JsonDockRepository. Persists the dock configuration to
/// config.json using System.Text.Json with polymorphic @type discrimination,
/// keeping the JSON format identical to the original Java/Jackson output.
///
/// The config file lives in %APPDATA%\MobreadModernDock\config.json (Windows-specific).
/// A future Infrastructure.MacOS/Linux sibling would use a different base path.
/// </summary>
public sealed class JsonDockRepository : IDockRepository
{
    private const string ConfigFileName = "config.json";
    private const string AppDataFolder = "MobreadModernDock";
    private const string LegacyAppDataFolder = "CedroModernDock";

    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// True when Load() had to create a fresh default config because none
    /// existed (first run) or the existing file was corrupt/empty.
    /// </summary>
    public bool WasDefaultCreated { get; private set; }

    public JsonDockRepository() : this(GetDefaultConfigPath()) { }

    public JsonDockRepository(string configFilePath)
    {
        _configFilePath = configFilePath;
        _serializerOptions = CreateSerializerOptions();
    }

    public void Save(DockModel model)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configFilePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(model, _serializerOptions);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving the DockModel: {e.Message}");
        }
    }

    public DockModel Load()
    {
        if (File.Exists(_configFilePath) && new FileInfo(_configFilePath).Length > 0)
        {
            try
            {
                string json = File.ReadAllText(_configFilePath);
                var model = JsonSerializer.Deserialize<DockModel>(json, _serializerOptions);
                if (model != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[JsonDockRepository] config.json loaded from: {_configFilePath}");
                    return model;
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Error reading config.json, creating default: {e.Message}");
                return CreateAndSaveDefault();
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"config.json not found or empty. Creating default at: {_configFilePath}");
        return CreateAndSaveDefault();
    }

    private DockModel CreateAndSaveDefault()
    {
        WasDefaultCreated = true;
        var model = new DockModel();
        model.LoadDefaultItems();
        Save(model);
        return model;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Use camelCase to match the original Java/Jackson config.json format
            // (label, path, module, iconsSize, dockColorRGB, etc.). Explicit
            // [JsonPropertyName] attributes on DockModel take precedence and are
            // kept for clarity, but the policy ensures DockItem subtypes' plain
            // auto-properties (Label, Path, Module) also match camelCase keys.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        // Unknown properties are ignored by default in STJ (no FAIL_ON_UNKNOWN_PROPERTIES equivalent needed)
        return options;
    }

    /// <summary>
    /// The exact serializer settings used for config.json, so export/import
    /// produces files interchangeable with the live config.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    /// <summary>Absolute path of the config file this repository reads and writes.</summary>
    public string ConfigFilePath => _configFilePath;

    private static string GetDefaultConfigPath()
    {
        string configDir = Adapters.AppDataLocator.Root;
        if (!Adapters.AppDataLocator.IsPortable)
        {
            string? appDataPath = Path.GetDirectoryName(configDir);
            if (!string.IsNullOrEmpty(appDataPath)) MigrateLegacyAppData(appDataPath, configDir);
        }
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        return Path.Combine(configDir, ConfigFileName);
    }

    /// <summary>
    /// One-time carry-over from the pre-rename %APPDATA%\CedroModernDock folder:
    /// if the new folder doesn't exist yet and the legacy one does, copy it
    /// (config + icon cache) so the first launch after upgrading keeps the
    /// user's shortcuts and settings. The legacy folder is left in place.
    /// </summary>
    private static void MigrateLegacyAppData(string appDataPath, string configDir)
    {
        try
        {
            string legacyDir = Path.Combine(appDataPath, LegacyAppDataFolder);
            if (Directory.Exists(configDir) || !Directory.Exists(legacyDir)) return;
            CopyDirectory(legacyDir, configDir);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy app-data migration failed: {e.Message}");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
