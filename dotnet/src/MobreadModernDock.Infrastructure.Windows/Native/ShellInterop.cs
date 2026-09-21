using System.Runtime.InteropServices;
using System.Text;

namespace MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// P/Invoke additions for Windows Shell icon extraction.
/// </summary>
internal static class Shell32
{
    private const int SHGFI_ICON = 0x000000100;
    internal const int SHGFI_SYSICONINDEX = 0x000004000;
    internal const int SHGFI_USEFILEATTRIBUTES = 0x000000010;
    internal const int FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    internal const int FILE_ATTRIBUTE_NORMAL = 0x00000080;
    internal const int SHIL_EXTRALARGE = 0x2;
    internal const int SHIL_JUMBO = 0x4;
    internal const int ILD_TRANSPARENT = 0x00000001;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SHGetFileInfoW(
        string pszPath, int dwFileAttributes, ref SHFILEINFO psfi, int cbFileInfo, int uFlags);

    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern int SHGetImageList(int iImageList, in Guid riid, out IntPtr ppv);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public int dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

internal static class User32Icon
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int PrivateExtractIcons(
        string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        [Out] IntPtr[] phicon, [Out] int[] piconid, int nIcons, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}

internal static class Comctl32Icon
{
    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);
}
