namespace MobreadModernDock.Core.Application;

using System.Text;

/// <summary>
/// Splits a <c>*.customDestinations-ms</c> file (the app-declared half of a
/// Windows jump list, written by <c>ICustomDestinationList</c>) into its
/// categories and the raw <c>.lnk</c> streams inside them. Pure byte work so
/// it can be tested; resolving the streams into targets and titles needs COM
/// and lives in Infrastructure.
///
/// Layout: a 12-byte header (version, category count, reserved), then per
/// category a type dword - 0 = named custom category (uint16 name length,
/// UTF-16 name, entry count), 1 = known category (Frequent/Recent, no
/// entries), 2 = the unnamed Tasks group (entry count) - followed by the
/// entries, each a CLSID and the persisted object, and a footer marker.
/// Only <c>ShellLink</c> entries are understood; a category holding anything
/// else is skipped to its footer.
/// </summary>
public static class JumpListFile
{
    /// <summary>Marker after every category.</summary>
    public static readonly byte[] Footer = { 0xAB, 0xFB, 0xBF, 0xBA };

    /// <summary>CLSID_ShellLink {00021401-0000-0000-C000-000000000046}, as persisted.</summary>
    public static readonly byte[] ShellLinkClsid =
        { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 };

    public sealed record RawCategory(string? Name, IReadOnlyList<byte[]> Links)
    {
        public bool IsTasks => Name == null;
    }

    public static IReadOnlyList<RawCategory> Parse(byte[] data)
    {
        var result = new List<RawCategory>();
        if (data.Length < 12) return result;
        int categories = BitConverter.ToInt32(data, 4);
        int pos = 12;

        for (int c = 0; c < categories && pos + 4 <= data.Length; c++)
        {
            int type = BitConverter.ToInt32(data, pos); pos += 4;
            string? name = null;
            int count;
            switch (type)
            {
                case 0:
                    if (pos + 2 > data.Length) return result;
                    int chars = BitConverter.ToUInt16(data, pos); pos += 2;
                    if (pos + chars * 2 + 4 > data.Length) return result;
                    name = Encoding.Unicode.GetString(data, pos, chars * 2); pos += chars * 2;
                    count = BitConverter.ToInt32(data, pos); pos += 4;
                    break;
                case 1:
                    // Known category: a dword saying which one, no entries.
                    pos = SkipToAfterFooter(data, pos + 4);
                    continue;
                case 2:
                    if (pos + 4 > data.Length) return result;
                    count = BitConverter.ToInt32(data, pos); pos += 4;
                    break;
                default:
                    return result;
            }

            var links = new List<byte[]>();
            bool understood = true;
            for (int i = 0; i < count; i++)
            {
                if (!Matches(data, pos, ShellLinkClsid)) { understood = false; break; }
                int start = pos + ShellLinkClsid.Length;
                // The persisted link carries no length; the next entry starts
                // with its CLSID immediately followed by a .lnk header, and
                // the category ends at the footer.
                int end = i == count - 1 ? IndexOf(data, Footer, start) : NextLinkEntry(data, start);
                if (end < 0) end = IndexOf(data, Footer, start);
                if (end < 0) end = data.Length;
                links.Add(data.AsSpan(start, end - start).ToArray());
                pos = end;
            }
            if (understood && links.Count > 0)
                result.Add(new RawCategory(name, links));
            pos = SkipToAfterFooter(data, pos);
        }
        return result;
    }

    private static int NextLinkEntry(byte[] data, int from)
    {
        // CLSID_ShellLink, then the .lnk header: size 0x4C and the same CLSID.
        int i = from;
        while ((i = IndexOf(data, ShellLinkClsid, i)) >= 0)
        {
            int hdr = i + ShellLinkClsid.Length;
            if (hdr + 4 + ShellLinkClsid.Length <= data.Length
                && BitConverter.ToInt32(data, hdr) == 0x4C
                && Matches(data, hdr + 4, ShellLinkClsid))
                return i;
            i += 1;
        }
        return -1;
    }

    private static int SkipToAfterFooter(byte[] data, int from)
    {
        int i = IndexOf(data, Footer, Math.Min(from, data.Length));
        return i < 0 ? data.Length : i + Footer.Length;
    }

    private static bool Matches(byte[] data, int at, byte[] pattern) =>
        at >= 0 && at + pattern.Length <= data.Length && data.AsSpan(at, pattern.Length).SequenceEqual(pattern);

    private static int IndexOf(byte[] data, byte[] pattern, int from)
    {
        if (from < 0 || from > data.Length) return -1;
        int i = data.AsSpan(from).IndexOf(pattern);
        return i < 0 ? -1 : from + i;
    }
}
