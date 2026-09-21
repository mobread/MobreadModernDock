namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.IO;

/// <summary>
/// #15 Portable mode. When a file named <c>portable.marker</c> (or an existing
/// <c>config.json</c>) sits next to the executable, all per-user data (config,
/// icon cache, crash log) lives in that folder instead of %APPDATA%.
/// Resolved once; every path consumer asks here.
/// </summary>
public static class AppDataLocator
{
    public const string AppFolderName = "MobreadModernDock";
    public const string PortableMarker = "portable.marker";

    private static readonly Lazy<(string Root, bool Portable)> Resolved = new(Resolve);

    /// <summary>Directory that holds config.json, iconsCache, crash-log.txt.</summary>
    public static string Root => Resolved.Value.Root;
    public static bool IsPortable => Resolved.Value.Portable;

    private static (string, bool) Resolve()
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(exeDir))
            {
                bool marker = File.Exists(Path.Combine(exeDir, PortableMarker));
                bool config = File.Exists(Path.Combine(exeDir, "config.json"));
                if (marker || config)
                {
                    // Make sure it's actually writable (Program Files isn't for a non-admin).
                    if (IsWritable(exeDir)) return (exeDir, true);
                }
            }
        }
        catch { }

        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return (Path.Combine(appData, AppFolderName), false);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
