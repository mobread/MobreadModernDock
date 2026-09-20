namespace MobreadModernDock.Core.Domain;

/// <summary>Port for resolving program/folder icons from the OS.</summary>
public interface IIconGateway
{
    string? ResolveProgramIcon(string executablePath);
    string? ResolveFolderIcon(string folderPath);

    /// <summary>Explorer-style icon for any file or folder, cached; null when unavailable.</summary>
    string? ResolveFileIcon(string path) => null;

    void CacheProgramIcon(string executablePath);
    void CacheFolderIcon(string folderPath);
}
