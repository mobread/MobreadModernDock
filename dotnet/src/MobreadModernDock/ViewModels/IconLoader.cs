using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// Helper for loading dock icons from two sources:
/// 1. Cached PNG files (program/folder icons extracted by WindowsIconExtractor)
/// 2. Avalonia asset resources (built-in icons: settings, my_computer, etc.)
/// </summary>
public static class IconLoader
{
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
            _ => null
        };
        return LoadFromAsset(MapResourcePath(resourcePath));
    }

    /// <summary>
    /// Maps a Java-style resource path (e.g. "/com/github/.../icons/settings.png")
    /// to the Avalonia asset path ("Assets/icons/settings.png").
    /// </summary>
    public static string? MapResourcePath(string? javaResourcePath)
    {
        if (string.IsNullOrEmpty(javaResourcePath))
            return null;

        // Extract the filename from the Java resource path.
        string fileName = Path.GetFileName(javaResourcePath);
        return $"Assets/icons/{fileName}";
    }
}
