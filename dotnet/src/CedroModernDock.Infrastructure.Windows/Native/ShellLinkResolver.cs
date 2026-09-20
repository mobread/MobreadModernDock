namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Resolves Windows <c>.lnk</c> shortcut files through the shell's
/// <c>IShellLink</c> COM interface: target path, arguments, working
/// directory and icon location. Handles the MSI "advertised" shortcut case
/// where the target path is empty by falling back to the icon location.
/// </summary>
public static class ShellLinkResolver
{
    public sealed record ResolvedShortcut(
        string TargetPath,
        string Arguments,
        string WorkingDirectory,
        string? IconPath,
        int IconIndex);

    public static bool IsShortcut(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    public static ResolvedShortcut? Resolve(string lnkPath)
    {
        if (!File.Exists(lnkPath)) return null;
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);

            var target = new StringBuilder(MAX_PATH);
            link.GetPath(target, target.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            var args = new StringBuilder(1024);
            link.GetArguments(args, args.Capacity);
            var workDir = new StringBuilder(MAX_PATH);
            link.GetWorkingDirectory(workDir, workDir.Capacity);
            var icon = new StringBuilder(MAX_PATH);
            link.GetIconLocation(icon, icon.Capacity, out int iconIndex);

            string targetPath = Environment.ExpandEnvironmentVariables(target.ToString());
            string? iconPath = icon.Length > 0 ? Environment.ExpandEnvironmentVariables(icon.ToString()) : null;

            // Advertised (MSI) shortcuts report no path; the shell resolves
            // them via the Darwin descriptor. Try SLGP_UNCPRIORITY / the
            // resolved target as a fallback before giving up on the path.
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                link.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_NOUPDATE);
                target.Clear();
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                targetPath = Environment.ExpandEnvironmentVariables(target.ToString());
            }

            targetPath = NormalizeTarget(targetPath, iconPath, lnkPath);

            return new ResolvedShortcut(
                targetPath,
                args.ToString(),
                Environment.ExpandEnvironmentVariables(workDir.ToString()),
                iconPath,
                iconIndex);
        }
        catch
        {
            return null;
        }
    }

    private const int MAX_PATH = 260;
    private const uint SLGP_RAWPATH = 0x4;
    private const uint SLR_NO_UI = 0x1;
    private const uint SLR_NOUPDATE = 0x8;

    /// <summary>
    /// Repairs targets that are not a launchable .exe:
    /// <list type="bullet">
    /// <item>MSI "advertised" shortcuts (e.g. WSL) report the icon file
    /// (<c>...\{GUID}\wsl.ico</c>) as the target. Look for an .exe with the
    /// same stem next to the shortcut's icon, in System32, or on PATH.</item>
    /// <item>Shell-object shortcuts (File Explorer) report no target at all
    /// but carry an .exe as their icon location; use that.</item>
    /// </list>
    /// Anything that still isn't an existing .exe is returned as-is so the
    /// caller can reject it.
    /// </summary>
    private static string NormalizeTarget(string targetPath, string? iconPath, string lnkPath)
    {
        if (IsExistingExe(targetPath)) return targetPath;

        // Empty target but the icon points at an exe (Explorer, Settings...).
        if (string.IsNullOrWhiteSpace(targetPath) && IsExistingExe(iconPath))
            return iconPath!;

        // Non-exe target (advertised shortcut): try <stem>.exe in known places.
        string stem = Path.GetFileNameWithoutExtension(
            !string.IsNullOrWhiteSpace(targetPath) ? targetPath : (iconPath ?? Path.GetFileNameWithoutExtension(lnkPath)));
        if (string.IsNullOrWhiteSpace(stem)) return targetPath;

        foreach (var candidate in CandidateExeLocations(stem, targetPath, iconPath))
            if (IsExistingExe(candidate)) return candidate;

        return targetPath;
    }

    private static IEnumerable<string> CandidateExeLocations(string stem, string targetPath, string? iconPath)
    {
        string exe = stem + ".exe";
        foreach (var dirSource in new[] { targetPath, iconPath })
        {
            string? dir = string.IsNullOrWhiteSpace(dirSource) ? null : Path.GetDirectoryName(dirSource);
            if (dir != null) yield return Path.Combine(dir, exe);
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), exe);
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(p.Trim(), exe);
    }

    private static bool IsExistingExe(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
