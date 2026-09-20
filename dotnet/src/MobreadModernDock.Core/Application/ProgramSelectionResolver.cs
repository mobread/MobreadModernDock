namespace MobreadModernDock.Core.Application;

using System.IO;

/// <summary>
/// Direct port of ProgramSelectionResolver. Resolves a user-selected executable,
/// handling Squirrel-installed apps (e.g. Discord) where the user picks Update.exe
/// but the real app lives inside an app-x.y.z subfolder.
/// </summary>
public static class ProgramSelectionResolver
{
    public sealed record ResolvedProgramSelection(string ExecutablePath, string Label);

    public static ResolvedProgramSelection Resolve(string selectedExecutable)
    {
        var normalizedPath = Path.GetFullPath(selectedExecutable);
        var resolvedExecutable = ResolveSquirrelExecutable(normalizedPath);
        return new ResolvedProgramSelection(
            resolvedExecutable,
            StripExtension(Path.GetFileName(resolvedExecutable))
        );
    }

    private static string ResolveSquirrelExecutable(string selectedExecutable)
    {
        if (!IsUpdaterExecutable(selectedExecutable))
            return selectedExecutable;

        string? installDir = Path.GetDirectoryName(selectedExecutable);
        if (installDir == null)
            return selectedExecutable;

        string installFolderName = Path.GetFileName(installDir) ?? "";
        var appDirectories = ListAppDirectories(installDir);

        foreach (var appDirectory in appDirectories)
        {
            string preferredExecutable = Path.Combine(appDirectory, installFolderName + ".exe");
            if (File.Exists(preferredExecutable))
                return preferredExecutable;

            string? fallbackExecutable = FindFirstExecutable(appDirectory);
            if (fallbackExecutable != null)
                return fallbackExecutable;
        }

        return selectedExecutable;
    }

    private static bool IsUpdaterExecutable(string executablePath) =>
        string.Equals(Path.GetFileName(executablePath), "Update.exe", StringComparison.OrdinalIgnoreCase);

    private static List<string> ListAppDirectories(string installDir)
    {
        try
        {
            return Directory.GetDirectories(installDir)
                .Where(d => Path.GetFileName(d)?.StartsWith("app-", StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(GetLastModifiedTime)
                .ToList();
        }
        catch (IOException)
        {
            return new List<string>();
        }
    }

    private static long GetLastModifiedTime(string path)
    {
        try
        {
            return new FileInfo(path).LastWriteTimeUtc.Ticks;
        }
        catch (IOException)
        {
            return long.MinValue;
        }
    }

    private static string? FindFirstExecutable(string appDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(appDirectory, "*.exe")
                .Where(f => !string.Equals(Path.GetFileName(f), "Update.exe", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string StripExtension(string fileName)
    {
        int lastDot = fileName.LastIndexOf('.');
        return lastDot > 0 ? fileName[..lastDot] : fileName;
    }
}
