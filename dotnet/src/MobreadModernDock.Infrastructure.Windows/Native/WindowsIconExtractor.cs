namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Xml;

/// <summary>
/// Extracts high-resolution (256x256)
/// icons from executables and folders using the Windows Shell API, and caches
/// them as PNG files in %APPDATA%\MobreadModernDock\iconsCache.
/// Uses System.Drawing.Icon.FromHandle() for HICON→Bitmap conversion.
/// </summary>
public static class WindowsIconExtractor
{
    private const int IconSize = 256;
    private const string IidImageList = "46EB5926-582E-4017-9FDF-E8998DAA0950";
    private static readonly string CacheDir = GetCacheDirectory();

    public static string? GetCachedIconPath(string exePath) => GetCachedPath(exePath, "program");

    public static string? GetCachedFolderIconPath(string folderPath) => GetCachedPath(folderPath, "folder_v3");

    public static string? ExtractAndCacheIcon(string exePath)
    {
        try
        {
            string? cachedIconPath = GetCachedIconPath(exePath);
            if (cachedIconPath == null) return null;
            if (File.Exists(cachedIconPath)) return cachedIconPath;

            if (!ExtractIconWithShellApi(exePath, cachedIconPath))
            {
                try { File.Delete(cachedIconPath); } catch { }
                return null;
            }
            return cachedIconPath;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAndCacheIcon error: {e.Message}");
            return null;
        }
    }

    public static string? ExtractAndCacheFolderIcon(string folderPath)
    {
        try
        {
            string? cachedIconPath = GetCachedFolderIconPath(folderPath);
            if (cachedIconPath == null) return null;
            if (File.Exists(cachedIconPath)) return cachedIconPath;
            if (!Directory.Exists(folderPath)) return null;

            using var image = ExtractFolderIconWithShellApi(folderPath);
            if (image == null) return null;

            image.Save(cachedIconPath, ImageFormat.Png);
            return File.Exists(cachedIconPath) && new FileInfo(cachedIconPath).Length > 0
                ? cachedIconPath : null;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAndCacheFolderIcon error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback for modern/UWP apps (e.g. Windows Settings) whose EXE ships no
    /// classic icon resource: grabs the running window's own icon (WM_GETICON,
    /// falling back to the class icon) and caches it under the exe's icon path.
    /// The HICON is shared/owned by the window, so it is never destroyed here.
    /// </summary>
    public static string? ExtractAndCacheWindowIcon(string exePath, IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return null;
            string? cachedIconPath = GetCachedIconPath(exePath);
            if (cachedIconPath == null) return null;
            if (File.Exists(cachedIconPath)) return cachedIconPath;

            IntPtr hIcon = User32.SendMessage(hwnd, Win32Constants.WM_GETICON, Win32Constants.ICON_BIG, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = User32.GetClassLongPtr(hwnd, Win32Constants.GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                hIcon = User32.SendMessage(hwnd, Win32Constants.WM_GETICON, Win32Constants.ICON_SMALL2, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                return null;

            using var icon = Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(cachedIconPath, ImageFormat.Png);
            return File.Exists(cachedIconPath) && new FileInfo(cachedIconPath).Length > 0
                ? cachedIconPath : null;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAndCacheWindowIcon error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback for UWP/AppX apps (Settings, Snipping Tool, ...): their icons
    /// live in the package manifest, not in the EXE. Walks up from the exe to
    /// the package root (where AppxManifest.xml lives), reads the
    /// Square44x44Logo asset (with scale variants) and copies it into the
    /// icon cache under the exe's key.
    /// </summary>
    public static string? ExtractAndCacheAppxIcon(string exePath)
    {
        try
        {
            string? cachedIconPath = GetCachedIconPath(exePath);
            if (cachedIconPath == null) return null;
            if (File.Exists(cachedIconPath)) return cachedIconPath;

            string? assetPath = ResolveAppxLogoAsset(exePath);
            if (assetPath == null) return null;

            File.Copy(assetPath, cachedIconPath, overwrite: true);
            return File.Exists(cachedIconPath) && new FileInfo(cachedIconPath).Length > 0
                ? cachedIconPath : null;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAndCacheAppxIcon error: {e.Message}");
            return null;
        }
    }

    private static string? ResolveAppxLogoAsset(string exePath)
    {
        // Find the package root: the directory containing AppxManifest.xml.
        var dir = new DirectoryInfo(Path.GetDirectoryName(exePath) ?? "");
        string? root = null;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AppxManifest.xml")))
            {
                root = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }
        if (root == null) return null;

        var doc = new XmlDocument();
        doc.Load(Path.Combine(root, "AppxManifest.xml"));

        // VisualElements element (any namespace) and its logo attributes.
        XmlElement? visualElements = FindElementByLocalName(doc.DocumentElement, "VisualElements");
        string? relPath = null;
        if (visualElements != null)
        {
            foreach (string attr in new[] { "Square44x44Logo", "Square150x150Logo", "Square71x71Logo" })
            {
                if (!string.IsNullOrEmpty(visualElements.GetAttribute(attr)))
                {
                    relPath = visualElements.GetAttribute(attr);
                    break;
                }
            }
        }
        if (relPath == null)
        {
            XmlElement? logo = FindElementByLocalName(doc.DocumentElement, "Logo");
            relPath = logo?.InnerText.Trim();
        }
        if (string.IsNullOrEmpty(relPath)) return null;

        string relative = relPath.Replace('/', Path.DirectorySeparatorChar);
        string basePath = Path.Combine(root, relative);
        foreach (string candidate in EnumerateAssetCandidates(basePath))
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Enumerates the asset file candidates in priority order: the base file
    /// first, then the scale variants (Windows convention: "logo.scale-200.png"
    /// and "logo_scale-200.png"), largest scale first.
    /// </summary>
    private static IEnumerable<string> EnumerateAssetCandidates(string basePath)
    {
        yield return basePath;
        string dir = Path.GetDirectoryName(basePath) ?? "";
        string fileName = Path.GetFileName(basePath);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        foreach (int scale in new[] { 400, 300, 240, 200, 150, 125, 100 })
        {
            yield return Path.Combine(dir, $"{stem}.scale-{scale}{ext}");
            yield return Path.Combine(dir, $"{stem}_scale-{scale}{ext}");
        }
    }

    private static XmlElement? FindElementByLocalName(XmlElement? parent, string localName)
    {
        if (parent == null) return null;
        foreach (XmlNode node in parent.ChildNodes)
        {
            if (node is XmlElement element)
            {
                if (element.LocalName == localName) return element;
                XmlElement? found = FindElementByLocalName(element, localName);
                if (found != null) return found;
            }
        }
        return null;
    }
    // --- private methods inserted below ---

    private static bool ExtractIconWithShellApi(string exePath, string outputPath)
    {
        var icons = new IntPtr[1];
        var iconIds = new int[1];

        int extracted = User32Icon.PrivateExtractIcons(
            exePath, 0, IconSize, IconSize, icons, iconIds, 1, 0);

        if (extracted <= 0 || icons[0] == IntPtr.Zero)
            return false;

        try
        {
            // Icon.FromHandle converts HICON → Bitmap (no manual GDI pixel loop needed).
            using var icon = Icon.FromHandle(icons[0]);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(outputPath, ImageFormat.Png);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            User32Icon.DestroyIcon(icons[0]);
        }
    }

    private static Bitmap? ExtractFolderIconWithShellApi(string folderPath)
    {
        return ExtractShellIcon(folderPath, isDirectory: true, Shell32.SHIL_JUMBO);
    }

    /// <summary>
    /// Icon for any file or folder via the shell image list (same icon
    /// Explorer shows). Cached as PNG under the icon cache; returns the
    /// cached path or null.
    /// </summary>
    public static string? ExtractAndCacheFileIcon(string path)
    {
        try
        {
            string? cached = GetCachedPath(path, "file_v1");
            if (cached == null) return null;
            if (File.Exists(cached)) return cached;
            bool isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path)) return null;
            using var bmp = ExtractShellIcon(path, isDir, Shell32.SHIL_EXTRALARGE);
            if (bmp == null) return null;
            bmp.Save(cached, ImageFormat.Png);
            return cached;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractAndCacheFileIcon error: {e.Message}");
            return null;
        }
    }

    private static Bitmap? ExtractShellIcon(string path, bool isDirectory, int imageListSize)
    {
        var shFileInfo = new SHFILEINFO();
        int attrs = isDirectory ? Shell32.FILE_ATTRIBUTE_DIRECTORY : Shell32.FILE_ATTRIBUTE_NORMAL;
        // SHGFI_USEFILEATTRIBUTES lets the shell pick an icon by extension
        // without opening the file (fast, and works for files on slow media).
        IntPtr result = Shell32.SHGetFileInfoW(
            path, attrs, ref shFileInfo,
            System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
            Shell32.SHGFI_SYSICONINDEX | (isDirectory ? 0 : Shell32.SHGFI_USEFILEATTRIBUTES));

        if (result == IntPtr.Zero)
            return null;

        var iid = new Guid(IidImageList);
        int hr = Shell32.SHGetImageList(imageListSize, in iid, out IntPtr imageList);
        if (hr != 0 || imageList == IntPtr.Zero)
            return null;

        IntPtr hIcon = Comctl32Icon.ImageList_GetIcon(
            imageList, shFileInfo.iIcon, Shell32.ILD_TRANSPARENT);
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            var icon = Icon.FromHandle(hIcon);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            User32Icon.DestroyIcon(hIcon);
        }
    }

    private static string? GetCachedPath(string inputPath, string kind)
    {
        try
        {
            string fileName = $"{kind}_{GetHashedFileName(inputPath)}.png";
            return Path.Combine(CacheDir, fileName);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"GetCachedPath failed: {e.Message}");
            return null;
        }
    }

    private static string GetHashedFileName(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetCacheDirectory()
    {
        string cacheDir = Path.Combine(Adapters.AppDataLocator.Root, "iconsCache");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }
}
