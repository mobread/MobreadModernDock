namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.IO;

/// <summary>
/// Packaged (MSIX / Store) apps install under a version-stamped directory,
/// <c>WindowsApps\Name_1.2.3.0_x64__PublisherId\app.exe</c>, and every
/// update moves them to a new one. A path pinned last month therefore stops
/// existing - the dock could neither launch it nor extract its icon. This
/// finds the currently installed location for the same package family and
/// re-roots the path onto it.
///
/// <c>C:\Program Files\WindowsApps</c> itself cannot be enumerated by a
/// normal user (ACL), so the installed location comes from the deployment
/// API (<c>PackageManager.FindPackagesForUser</c>, ~5 ms); a directory scan
/// is only a fallback for the tests and for odd installs.
/// </summary>
public static class PackagedAppPaths
{
    /// <summary>Overridable for tests; production asks the deployment API.</summary>
    public static Func<string, string, IEnumerable<(Version Version, string Location)>> InstalledVersions = QueryPackageManager;

    /// <summary>The production lookup (deployment API), for tests that stubbed it.</summary>
    public static Func<string, string, IEnumerable<(Version Version, string Location)>> DefaultInstalledVersions => QueryPackageManager;

    /// <summary>
    /// <paramref name="exePath"/> itself when it exists or is not a packaged
    /// app path; otherwise the same relative path under the newest installed
    /// version of that package, or the original path when none is found.
    /// </summary>
    public static string ResolveCurrentVersion(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || File.Exists(exePath)) return exePath;
            var parts = exePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int i = Array.FindIndex(parts, s => string.Equals(s, "WindowsApps", StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 2 > parts.Length) return exePath;
            if (!TrySplitFullName(parts[i + 1], out string name, out string arch, out string publisher)) return exePath;

            string relative = string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 2));
            string family = name + "_" + publisher;

            var candidates = InstalledVersions(family, arch).ToList();
            if (candidates.Count == 0)
            {
                string root = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));
                candidates = ScanDirectory(root, name, arch, publisher).ToList();
            }
            foreach (var (_, location) in candidates.OrderByDescending(c => c.Version))
            {
                string candidate = Path.Combine(location, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return exePath;
        }
        catch
        {
            return exePath;
        }
    }

    private static IEnumerable<(Version, string)> QueryPackageManager(string family, string arch)
    {
        var result = new List<(Version, string)>();
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            foreach (var p in pm.FindPackagesForUser("", family))
            {
                if (!string.Equals(p.Id.Architecture.ToString(), arch, StringComparison.OrdinalIgnoreCase)) continue;
                var v = p.Id.Version;
                string? loc = null;
                try { loc = p.InstalledLocation?.Path; } catch { }
                if (!string.IsNullOrEmpty(loc))
                    result.Add((new Version(v.Major, v.Minor, v.Build, v.Revision), loc));
            }
        }
        catch { /* no WinRT, or the API refused: fall back to scanning */ }
        return result;
    }

    private static IEnumerable<(Version, string)> ScanDirectory(string root, string name, string arch, string publisher)
    {
        if (!Directory.Exists(root)) yield break;
        string prefix = name + "_";
        string suffix = "_" + arch + "__" + publisher;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, prefix + "*" + suffix).ToList(); }
        catch { yield break; }
        foreach (var dir in dirs)
        {
            string leaf = Path.GetFileName(dir);
            if (leaf.Length <= prefix.Length + suffix.Length) continue;
            string middle = leaf.Substring(prefix.Length, leaf.Length - prefix.Length - suffix.Length);
            if (Version.TryParse(middle, out var v)) yield return (v, dir);
        }
    }

    /// <summary>Name_Version_Arch__PublisherId → (Name, Arch, PublisherId).</summary>
    public static bool TrySplitFullName(string fullName, out string name, out string arch, out string publisher)
    {
        name = arch = publisher = "";
        int dbl = fullName.IndexOf("__", StringComparison.Ordinal);
        if (dbl <= 0) return false;
        publisher = fullName[(dbl + 2)..];
        string head = fullName[..dbl];              // Name_Version_Arch
        int lastUnderscore = head.LastIndexOf('_');
        if (lastUnderscore <= 0) return false;
        arch = head[(lastUnderscore + 1)..];
        string nameAndVersion = head[..lastUnderscore];
        int firstUnderscore = nameAndVersion.IndexOf('_');
        if (firstUnderscore <= 0) return false;
        name = nameAndVersion[..firstUnderscore];
        return publisher.Length > 0 && arch.Length > 0;
    }
}
