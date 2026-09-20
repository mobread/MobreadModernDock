namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using MobreadModernDock.Core.Domain;

/// <summary>Direct port of DefaultFolderLauncher.java.</summary>
public class WindowsFolderLauncher : IFolderLauncher
{
    public void Launch(string folderPath, string label)
    {
        Debug.WriteLine($"{label} Clicked");

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Debug.WriteLine($"Folder path not defined for: {label}");
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            Debug.WriteLine($"Folder path is invalid: {folderPath}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folderPath,
                UseShellExecute = true
            });
            Debug.WriteLine($"Opening folder: {label}");
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to open folder: {label}");
            Debug.WriteLine($"Path: {folderPath}");
            Debug.WriteLine($"Error: {e.Message}");
        }
    }
}
