namespace CedroModernDock.Infrastructure.Windows.Adapters;

using CedroModernDock.Core.Domain;
using CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Adapter wrapping WindowsIconExtractor to implement IIconGateway.
/// Direct port of CachedWindowsIconGateway.java.
/// </summary>
public class CachedWindowsIconGateway : IIconGateway
{
    public string? ResolveProgramIcon(string executablePath)
        => WindowsIconExtractor.GetCachedIconPath(executablePath);

    public string? ResolveFileIcon(string path)
        => WindowsIconExtractor.ExtractAndCacheFileIcon(path);

    public string? ResolveFolderIcon(string folderPath)
        => WindowsIconExtractor.GetCachedFolderIconPath(folderPath);

    public void CacheProgramIcon(string executablePath)
        => WindowsIconExtractor.ExtractAndCacheIcon(executablePath);

    public void CacheFolderIcon(string folderPath)
        => WindowsIconExtractor.ExtractAndCacheFolderIcon(folderPath);
}
