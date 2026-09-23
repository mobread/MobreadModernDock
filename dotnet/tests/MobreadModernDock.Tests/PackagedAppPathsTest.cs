namespace MobreadModernDock.Tests;

using MobreadModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Packaged (Store) apps move to a new version-stamped directory on every
/// update, so a pinned path goes stale: no icon and no launch. The resolver
/// re-roots such a path onto the installed version.
/// </summary>
public class PackagedAppPathsTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mbd-" + Guid.NewGuid().ToString("N"), "WindowsApps");

    public PackagedAppPathsTest()
    {
        // The deployment API would find the machine's real packages; these
        // tests exercise the directory-scan path against a temp tree.
        PackagedAppPaths.InstalledVersions = (_, _) => Array.Empty<(Version, string)>();
    }

    public void Dispose() => Cleanup();

    private string MakePackage(string fullName, string relativeExe)
    {
        string exe = Path.Combine(_root, fullName, relativeExe);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, new byte[] { 1 });
        return exe;
    }

    [Fact]
    public void SplitsAFullPackageName()
    {
        Assert.True(PackagedAppPaths.TrySplitFullName("Claude_2.7032.0.0_x64__pzs8sxrjxfjjc", out var n, out var a, out var p));
        Assert.Equal("Claude", n);
        Assert.Equal("x64", a);
        Assert.Equal("pzs8sxrjxfjjc", p);

        Assert.True(PackagedAppPaths.TrySplitFullName("Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe", out n, out a, out p));
        Assert.Equal("Microsoft.WindowsTerminal", n);
        Assert.Equal("x64", a);

        Assert.False(PackagedAppPaths.TrySplitFullName("Microsoft.WindowsTerminal_8wekyb3d8bbwe", out _, out _, out _)); // alias dir, no version
        Assert.False(PackagedAppPaths.TrySplitFullName("chrome", out _, out _, out _));
    }

    [Fact]
    public void AStalePinResolvesToTheNewestInstalledVersion()
    {
        MakePackage("Claude_2.2553.1.0_x64__pzs8sxrjxfjjc", @"app\claude.exe"); // will be deleted: the old version
        string v2 = MakePackage("Claude_2.7032.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");
        string v3 = MakePackage("Claude_2.10000.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");
        string stale = Path.Combine(_root, "Claude_2.2553.1.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");
        Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(stale)!)!, true);

        Assert.Equal(v3, PackagedAppPaths.ResolveCurrentVersion(stale));
        Assert.NotEqual(v2, PackagedAppPaths.ResolveCurrentVersion(stale)); // numeric, not lexical: 2.10000 > 2.7032
        Cleanup();
    }

    [Fact]
    public void AnExistingPathIsReturnedUnchanged()
    {
        string exe = MakePackage("Claude_2.7032.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");
        Assert.Equal(exe, PackagedAppPaths.ResolveCurrentVersion(exe));
        Cleanup();
    }

    [Fact]
    public void DoesNotCrossPackageFamiliesOrArchitectures()
    {
        MakePackage("Claude_9.0.0.0_x86__pzs8sxrjxfjjc", @"app\claude.exe");
        MakePackage("Claude_9.0.0.0_x64__otherpublisher", @"app\claude.exe");
        string stale = Path.Combine(_root, "Claude_1.0.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");

        Assert.Equal(stale, PackagedAppPaths.ResolveCurrentVersion(stale));
        Cleanup();
    }

    [Fact]
    public void NonPackagedAndMissingPathsPassThrough()
    {
        Assert.Equal(@"C:\nope\app.exe", PackagedAppPaths.ResolveCurrentVersion(@"C:\nope\app.exe"));
        Assert.Equal("", PackagedAppPaths.ResolveCurrentVersion(""));
    }

    [Fact]
    public void ThePackageManagerAnswerIsPreferredOverScanning()
    {
        string installed = MakePackage("Claude_3.0.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");
        PackagedAppPaths.InstalledVersions = (family, arch) =>
        {
            Assert.Equal("Claude_pzs8sxrjxfjjc", family);
            Assert.Equal("x64", arch);
            return new[] { (new Version(3, 0, 0, 0), Path.GetDirectoryName(Path.GetDirectoryName(installed))!) };
        };
        string stale = Path.Combine(_root, "Claude_1.0.0.0_x64__pzs8sxrjxfjjc", @"app\claude.exe");

        Assert.Equal(installed, PackagedAppPaths.ResolveCurrentVersion(stale));
    }

    /// <summary>Real machine: the API is asked. Only meaningful where Claude is installed.</summary>
    [Fact]
    public void ResolvesARealStalePackagePathWhenThePackageIsInstalled()
    {
        PackagedAppPaths.InstalledVersions = PackagedAppPaths.DefaultInstalledVersions;
        const string stale = @"C:\Program Files\WindowsApps\Claude_0.0.0.1_x64__pzs8sxrjxfjjc\app\claude.exe";
        string resolved = PackagedAppPaths.ResolveCurrentVersion(stale);
        if (resolved == stale) return; // not installed here
        Assert.True(File.Exists(resolved));
        Assert.EndsWith(@"\app\claude.exe", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claude_", resolved);
    }

    private void Cleanup()
    {
        try { Directory.Delete(Path.GetDirectoryName(_root)!, true); } catch { }
    }
}
