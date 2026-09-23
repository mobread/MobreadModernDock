using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// Helper for loading dock icons from three sources:
/// 1. A user-chosen custom icon (.png/.ico/.exe) set per dock item
/// 2. Cached PNG files (program/folder icons extracted by WindowsIconExtractor)
/// 3. Avalonia asset resources (built-in icons: settings, my_computer, etc.)
/// </summary>
public static class IconLoader
{
    /// <summary>Extensions accepted by the "Change icon…" picker.</summary>
    public static readonly string[] CustomIconExtensions = { ".png", ".ico", ".exe", ".dll", ".jpg", ".jpeg", ".bmp" };

    /// <summary>
    /// Loads a user-chosen custom icon. Image files are decoded directly;
    /// an .exe/.dll goes through the shell icon extractor (and its cache), so
    /// users can point at "the icon of that other program". Returns null when
    /// the file is gone or unreadable, so callers fall back to the default.
    /// </summary>
    public static Bitmap? LoadCustomIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".exe" or ".dll")
        {
            var extracted = Infrastructure.Windows.Native.WindowsIconExtractor.ExtractAndCacheIcon(path);
            return LoadFromFile(extracted);
        }
        return LoadFromFile(path);
    }

    /// <summary>Loads a bitmap from a file path (cached icon PNG).</summary>
    public static Bitmap? LoadFromFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a bitmap from an Avalonia asset resource by its asset path
    /// (e.g. "Assets/icons/settings.png").
    /// </summary>
    public static Bitmap? LoadFromAsset(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;
        try
        {
            var uri = new Uri($"avares://MobreadModernDock/{assetPath}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Loads the bundled icon for a built-in Windows module.</summary>
    public static Bitmap? LoadWindowsModuleIcon(string moduleId)
    {
        string? resourcePath = moduleId switch
        {
            "start" => "/com/github/mobread/mobreadmoderndock/icons/start_menu.png",
            "mypc" => "/com/github/mobread/mobreadmoderndock/icons/my_computer.png",
            "trash" => "/com/github/mobread/mobreadmoderndock/icons/trash.png",
            "ctrlpnl" => "/com/github/mobread/mobreadmoderndock/icons/control.png",
            "pconfig" => "/com/github/mobread/mobreadmoderndock/icons/windows_settings.png",
            "shutdown" => "/com/github/mobread/mobreadmoderndock/icons/power_shutdown.png",
            "restart" => "/com/github/mobread/mobreadmoderndock/icons/power_restart.png",
            "signout" => "/com/github/mobread/mobreadmoderndock/icons/power_signout.png",
            "sleep" => "/com/github/mobread/mobreadmoderndock/icons/power_sleep.png",
            "lock" => "/com/github/mobread/mobreadmoderndock/icons/power_lock.png",
            "showdesktop" => "/com/github/mobread/mobreadmoderndock/icons/show_desktop.png",
            "taskview" => "/com/github/mobread/mobreadmoderndock/icons/task_view.png",
            _ => null
        };
        return LoadFromAsset(MapResourcePath(resourcePath));
    }

    /// <summary>
    /// Maps a legacy slash-separated resource path (e.g.
    /// "/com/github/.../icons/settings.png") stored in older configs to the
    /// Avalonia asset path ("Assets/icons/settings.png").
    /// </summary>
    public static string? MapResourcePath(string? resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        string fileName = Path.GetFileName(resourcePath);
        return $"Assets/icons/{fileName}";
    }
}
