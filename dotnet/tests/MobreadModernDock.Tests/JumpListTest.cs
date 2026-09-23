namespace MobreadModernDock.Tests;

using System.Text;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Infrastructure.Windows.Adapters;

/// <summary>
/// Jump lists: the "Tasks" (New window, New incognito window...) an app
/// registers with the taskbar. The dock shows them on Alt+right-click.
/// The file format is undocumented, so the parser is pinned by a synthetic
/// file built the way the shell writes them.
/// </summary>
public class JumpListTest
{
    private static readonly byte[] LnkHeader = { 0x4C, 0, 0, 0 };

    /// <summary>A stand-in .lnk body: header + CLSID + some payload (no footer inside).</summary>
    private static byte[] FakeLink(string payload)
    {
        var ms = new MemoryStream();
        ms.Write(LnkHeader); ms.Write(JumpListFile.ShellLinkClsid);
        ms.Write(Encoding.Unicode.GetBytes(payload));
        return ms.ToArray();
    }

    private static byte[] Build(params (string? Name, byte[][] Links)[] categories)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(2); w.Write(categories.Length); w.Write(0);
        foreach (var (name, links) in categories)
        {
            if (name == null) { w.Write(2); }
            else
            {
                w.Write(0);
                w.Write((ushort)name.Length);
                w.Write(Encoding.Unicode.GetBytes(name));
            }
            w.Write(links.Length);
            foreach (var l in links) { w.Write(JumpListFile.ShellLinkClsid); w.Write(l); }
            w.Write(JumpListFile.Footer);
        }
        return ms.ToArray();
    }

    [Fact]
    public void SplitsTasksIntoTheirLinks()
    {
        var data = Build((null, new[] { FakeLink("new window"), FakeLink("incognito") }));

        var cats = JumpListFile.Parse(data);

        var tasks = Assert.Single(cats);
        Assert.True(tasks.IsTasks);
        Assert.Equal(2, tasks.Links.Count);
        Assert.Equal(FakeLink("new window"), tasks.Links[0]);
        Assert.Equal(FakeLink("incognito"), tasks.Links[1]);
    }

    [Fact]
    public void ReadsNamedCategoriesAndKeepsOrder()
    {
        var data = Build(
            ("Profiles", new[] { FakeLink("pwsh"), FakeLink("cmd") }),
            (null, new[] { FakeLink("settings") }));

        var cats = JumpListFile.Parse(data);

        Assert.Equal(2, cats.Count);
        Assert.Equal("Profiles", cats[0].Name);
        Assert.Equal(2, cats[0].Links.Count);
        Assert.True(cats[1].IsTasks);
        Assert.Single(cats[1].Links);
    }

    [Fact]
    public void SkipsKnownCategoriesSuchAsRecent()
    {
        // Type 1 = known category (Frequent/Recent): a dword id, then the footer.
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(2); w.Write(2); w.Write(0);
        w.Write(1); w.Write(1); w.Write(JumpListFile.Footer);
        w.Write(2); w.Write(1); w.Write(JumpListFile.ShellLinkClsid); w.Write(FakeLink("only")); w.Write(JumpListFile.Footer);

        var cats = JumpListFile.Parse(ms.ToArray());

        var tasks = Assert.Single(cats);
        Assert.True(tasks.IsTasks);
    }

    [Fact]
    public void ToleratesEmptyAndTruncatedFiles()
    {
        Assert.Empty(JumpListFile.Parse(Array.Empty<byte>()));
        Assert.Empty(JumpListFile.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        var good = Build((null, new[] { FakeLink("x") }));
        // Chop mid-way: must not throw.
        _ = JumpListFile.Parse(good.AsSpan(0, good.Length / 2).ToArray());
    }

    [Fact]
    public void GatewayReturnsNothingForAnUnknownExeOrMissingFolder()
    {
        var missing = new CustomDestinationsJumpListGateway(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Empty(missing.GetCategories(@"C:\nope\app.exe"));
    }

    [Fact]
    public void PackagedAppsMatchByPackageFamilyAcrossAliasAndInstallPaths()
    {
        // A pinned Terminal is the versioned package exe; its jump list runs
        // the wt.exe alias. Same app, different paths.
        const string pinned = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";
        const string alias = @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\Microsoft.WindowsTerminal_8wekyb3d8bbwe\wt.exe";

        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe", CustomDestinationsJumpListGateway.PackageFamily(pinned));
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe", CustomDestinationsJumpListGateway.PackageFamily(alias));
        Assert.True(CustomDestinationsJumpListGateway.SameExe(pinned, alias));
        Assert.Null(CustomDestinationsJumpListGateway.PackageFamily(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
    }

    [Fact]
    public void UnpackagedAppsMatchOnlyOnTheExactPath()
    {
        Assert.True(CustomDestinationsJumpListGateway.SameExe(@"C:\A\app.exe", @"c:\a\APP.EXE"));
        Assert.False(CustomDestinationsJumpListGateway.SameExe(@"C:\A\app.exe", @"C:\A\other.exe"));
        // Two different packaged apps do not match on the WindowsApps prefix alone.
        Assert.False(CustomDestinationsJumpListGateway.SameExe(
            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe",
            @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_1.0_x64__8wekyb3d8bbwe\Calculator.exe"));
    }

    /// <summary>
    /// End-to-end against the machine's real jump lists. Only meaningful
    /// where Chrome has been run under this profile; otherwise it proves the
    /// no-match path.
    /// </summary>
    [Fact]
    public void GatewayResolvesRealLinksWhenPresent()
    {
        const string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var gateway = new CustomDestinationsJumpListGateway();
        var cats = gateway.GetCategories(chrome);
        if (cats.Count == 0) return;

        var tasks = cats.Single(c => c.IsTasks);
        Assert.Contains(tasks.Entries, e => e.Title.Contains("window", StringComparison.OrdinalIgnoreCase));
        Assert.All(tasks.Entries, e => Assert.Equal(chrome, e.ExecutablePath, ignoreCase: true));
    }
}
