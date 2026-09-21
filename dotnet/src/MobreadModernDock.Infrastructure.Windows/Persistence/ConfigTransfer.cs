namespace MobreadModernDock.Infrastructure.Windows.Persistence;

using System;
using System.IO;
using System.Text.Json;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

/// <summary>
/// Config export / import: writes the live dock configuration to a JSON file
/// the user picks, and reads one back.
///
/// The exported file <i>is</i> a config.json — same schema, same serializer —
/// so it can also be dropped into %APPDATA%\MobreadModernDock by hand. On
/// import the loaded model is copied into the live <see cref="DockModel"/>
/// in place (<see cref="DockModel.CopyFrom"/>), because services and view
/// models all hold a reference to that one instance.
/// </summary>
public static class ConfigTransfer
{
    /// <summary>Suggested file name for the save dialog.</summary>
    public static string SuggestedFileName =>
        $"mobread-dock-config-{DateTime.Now:yyyyMMdd}.json";

    /// <summary>Writes the current configuration to <paramref name="path"/>.</summary>
    public static bool Export(DockService dockService, string path)
    {
        try
        {
            string json = JsonSerializer.Serialize(
                dockService.GetDock(), JsonDockRepository.SerializerOptions);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigTransfer] export failed: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a config file and merges it into the live model, then persists.
    /// Returns false (leaving the current config untouched) when the file is
    /// missing, malformed, or not a dock config at all.
    /// </summary>
    public static bool Import(DockService dockService, string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            var imported = JsonSerializer.Deserialize<DockModel>(
                json, JsonDockRepository.SerializerOptions);
            // A JSON file that simply has no matching properties deserializes
            // to an all-default model rather than throwing; requiring at least
            // one item rejects the obvious "wrong file" case.
            if (imported == null || imported.Items.Count == 0) return false;

            dockService.GetDock().CopyFrom(imported);
            dockService.SaveChanges();
            return true;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigTransfer] import failed: {e.Message}");
            return false;
        }
    }
}
