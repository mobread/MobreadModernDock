namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Reads the jump list an application registered with the taskbar from
/// <c>%APPDATA%\Microsoft\Windows\Recent\CustomDestinations</c>.
///
/// The files are named by a hash of the app's AppUserModelID, which a pinned
/// exe does not reliably expose (Firefox derives one per profile), so the
/// folder is scanned instead and the newest file whose links run the exe
/// wins. Each link is loaded through <c>IShellLink</c> from memory; the menu
/// text is the link's <c>System.Title</c> property (what the taskbar shows),
/// falling back to the description.
/// </summary>
public sealed class CustomDestinationsJumpListGateway : IJumpListGateway
{
    private readonly string _folder;
    private readonly Dictionary<string, (DateTime Stamp, IReadOnlyList<JumpListCategory> Categories)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CustomDestinationsJumpListGateway()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Recent", "CustomDestinations")) { }

    public CustomDestinationsJumpListGateway(string folder) => _folder = folder;

    public IReadOnlyList<JumpListCategory> GetCategories(string executablePath)
    {
        try
        {
            if (!Directory.Exists(_folder)) return Array.Empty<JumpListCategory>();
            var files = new DirectoryInfo(_folder).GetFiles("*.customDestinations-ms")
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            if (files.Count == 0) return Array.Empty<JumpListCategory>();

            // Any change in the folder invalidates the cache; jump lists are
            // rewritten whenever the app updates them, which is rare.
            var newest = files[0].LastWriteTimeUtc;
            if (_cache.TryGetValue(executablePath, out var hit) && hit.Stamp == newest)
                return hit.Categories;

            var found = Array.Empty<JumpListCategory>() as IReadOnlyList<JumpListCategory>;
            foreach (var file in files)
            {
                var categories = Read(file.FullName);
                if (categories.Any(c => c.Entries.Any(e => SameExe(e.ExecutablePath, executablePath))))
                {
                    found = categories;
                    break;
                }
            }
            _cache[executablePath] = (newest, found);
            return found;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Jump list lookup failed for {executablePath}: {e.Message}");
            return Array.Empty<JumpListCategory>();
        }
    }

    /// <summary>
    /// Same program? Exact path, or - for packaged (MSIX) apps - the same
    /// package family: a pinned Terminal points at
    /// <c>WindowsApps\Microsoft.WindowsTerminal_1.24..._x64__8wekyb3d8bbwe\WindowsTerminal.exe</c>
    /// while its jump list links run the <c>wt.exe</c> alias under
    /// <c>WindowsApps\Microsoft.WindowsTerminal_8wekyb3d8bbwe\</c>.
    /// </summary>
    public static bool SameExe(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase)) return true;
        var fa = PackageFamily(a);
        return fa != null && string.Equals(fa, PackageFamily(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\'); } catch { return p; }
    }

    /// <summary>"Name_PublisherId" from any path under a WindowsApps folder, else null.</summary>
    public static string? PackageFamily(string path)
    {
        var parts = path.Split('\\', '/');
        int i = Array.FindIndex(parts, s => string.Equals(s, "WindowsApps", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= parts.Length) return null;
        string dir = parts[i + 1];
        // Full name: Name_Version_Arch__PublisherId; alias dir: Name_PublisherId.
        int dbl = dir.IndexOf("__", StringComparison.Ordinal);
        if (dbl > 0)
        {
            int firstUnderscore = dir.IndexOf('_');
            return firstUnderscore > 0 ? dir[..firstUnderscore] + "_" + dir[(dbl + 2)..] : null;
        }
        return dir.Contains('_') ? dir : null;
    }

    private static IReadOnlyList<JumpListCategory> Read(string path)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch { return Array.Empty<JumpListCategory>(); }

        var result = new List<JumpListCategory>();
        foreach (var raw in JumpListFile.Parse(data))
        {
            var entries = new List<JumpListEntry>();
            foreach (var link in raw.Links)
            {
                var entry = ResolveLink(link);
                if (entry != null) entries.Add(entry);
            }
            if (entries.Count > 0) result.Add(new JumpListCategory(raw.Name, entries));
        }
        return result;
    }

    /// <summary>Title, target and arguments of one persisted ShellLink stream.</summary>
    private static JumpListEntry? ResolveLink(byte[] lnk)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            var stream = new MemoryStream(lnk);
            ((IPersistStream)link).Load(new ComStream(stream));

            var target = new StringBuilder(MAX_PATH);
            link.GetPath(target, target.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            string exe = Environment.ExpandEnvironmentVariables(target.ToString());
            if (string.IsNullOrWhiteSpace(exe)) return null; // separator or shell-object entry

            var args = new StringBuilder(2048);
            link.GetArguments(args, args.Capacity);
            var desc = new StringBuilder(1024);
            link.GetDescription(desc, desc.Capacity);

            string? title = ReadTitle(link);
            if (string.IsNullOrWhiteSpace(title)) title = desc.ToString();
            if (string.IsNullOrWhiteSpace(title)) return null;

            return new JumpListEntry(title!, exe, args.ToString(),
                desc.Length > 0 && desc.ToString() != title ? desc.ToString() : null);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Jump list link unreadable: {e.Message}");
            return null;
        }
    }

    private static string? ReadTitle(object link)
    {
        if (link is not IPropertyStore store) return null;
        var key = PKEY_Title;
        store.GetValue(ref key, out var value);
        try
        {
            return value.vt == VT_LPWSTR && value.p != IntPtr.Zero ? Marshal.PtrToStringUni(value.p) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private const int MAX_PATH = 260;
    private const uint SLGP_RAWPATH = 0x4;
    private const ushort VT_LPWSTR = 31;
    private static readonly PROPERTYKEY PKEY_Title =
        new() { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt; public ushort r1, r2, r3;
        public IntPtr p; public IntPtr p2;
    }

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

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStream
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(IStream pStm);
        void Save(IStream pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
        void GetSizeMax(out ulong pcbSize);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    /// <summary>Minimal read-only IStream over a MemoryStream for IPersistStream.Load.</summary>
    private sealed class ComStream : IStream
    {
        private readonly MemoryStream _inner;
        public ComStream(MemoryStream inner) => _inner = inner;

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int n = _inner.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long pos = _inner.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
        }

        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG { cbSize = _inner.Length, type = 2 /* STGTY_STREAM */ };
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new NotSupportedException();
        public void SetSize(long libNewSize) => throw new NotSupportedException();
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) { }
        public void UnlockRegion(long libOffset, long cb, int dwLockType) { }
        public void Clone(out IStream ppstm) => throw new NotSupportedException();
    }
}
