namespace MobreadModernDock.Infrastructure.Windows.Persistence;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// Persists the dock configuration to config.json with System.Text.Json,
/// using polymorphic @type discrimination for the dock-item subtypes.
///
/// The config file lives in %APPDATA%\MobreadModernDock\config.json (Windows-specific),
/// or beside the executable in portable mode.
/// A future Infrastructure.MacOS/Linux sibling would use a different base path.
/// </summary>
public sealed class JsonDockRepository : IDockRepository
{
    private const string ConfigFileName = "config.json";
    private const string AppDataFolder = "MobreadModernDock";

    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<IEnumerable<DockItem>>? _firstRunSeed;

    /// <summary>
    /// True when Load() had to create a fresh default config because none
    /// existed (first run) or the existing file was corrupt/empty.
    /// </summary>
    public bool WasDefaultCreated { get; private set; }

    public JsonDockRepository() : this(GetDefaultConfigPath(), SeedFromTaskbarPins) { }

    /// <param name="firstRunSeed">
    /// Items a brand-new config starts with, over and above the Settings gear.
    /// Null means gear-only — which is what tests want, since the real seed
    /// reads the machine's taskbar pins through COM.
    /// </param>
    public JsonDockRepository(string configFilePath, Func<IEnumerable<DockItem>>? firstRunSeed = null)
    {
        _configFilePath = configFilePath;
        _serializerOptions = CreateSerializerOptions();
        _firstRunSeed = firstRunSeed;
    }

    /// <summary>
    /// The real first-run seed: the user's taskbar pins plus a couple of
    /// Windows modules. Failure is not fatal — a gear-only dock is still a
    /// working dock, so any shell error degrades to the old behaviour.
    /// </summary>
    private static IEnumerable<DockItem> SeedFromTaskbarPins()
    {
        try
        {
            return Core.Application.FirstRunDefaults.Compose(Native.TaskbarPinImporter.Enumerate());
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"First-run seed failed: {e.Message}");
            return Array.Empty<DockItem>();
        }
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
        model.LoadDefaultItems(_firstRunSeed?.Invoke());
        CacheSeedIcons(model);
        Save(model);
        return model;
    }

    /// <summary>
    /// Extracts icons for the seeded programs up front. Without this the very
    /// first frame of the dock — the one that decides whether the app looks
    /// working — draws placeholder squares until each icon is resolved.
    /// </summary>
    private static void CacheSeedIcons(DockModel model)
    {
        foreach (var item in model.Items)
        {
            try
            {
                if (item is DockProgramItemModel program)
                    Native.WindowsIconExtractor.ExtractAndCacheIcon(program.ExecutablePath);
                else if (item is DockFolderItemModel folder)
                    Native.WindowsIconExtractor.ExtractAndCacheFolderIcon(folder.FolderPath);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Seed icon cache failed: {e.Message}");
            }
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Use camelCase to match the established config.json format
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
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        return Path.Combine(configDir, ConfigFileName);
    }
}
