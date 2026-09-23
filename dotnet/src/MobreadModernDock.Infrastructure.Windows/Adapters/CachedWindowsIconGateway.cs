namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using MobreadModernDock.Core.Domain;
using MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Adapter wrapping WindowsIconExtractor to implement IIconGateway.
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
        => WindowsIconExtractor.ExtractAndCacheBestIcon(executablePath);

    public void CacheFolderIcon(string folderPath)
        => WindowsIconExtractor.ExtractAndCacheFolderIcon(folderPath);
}
