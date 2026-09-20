namespace MobreadModernDock.Core.Domain;

/// <summary>Port for opening folders in the OS file explorer.</summary>
public interface IFolderLauncher
{
    void Launch(string folderPath, string label);
}
