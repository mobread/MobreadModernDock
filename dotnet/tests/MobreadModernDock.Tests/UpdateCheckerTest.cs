namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// The in-app updater's decision logic. The parts worth pinning are the ones
/// that decide whether to run a downloaded executable, and the throttle that
/// keeps the app off GitHub's rate limit.
/// </summary>
public class UpdateCheckerTest
{
    private static UpdateChecker.Result WithBody(string body) =>
        new(new Version(1, 0, 0), new Version(1, 1, 0),
            "https://example.com/MobreadModernDock-1.1.0-x64.msi", null) { Body = body };

    /// <summary>The shape the real release notes use.</summary>
    private const string RealNotes = """
        ## Install

        ```
        MSI  8DC666B82BC153DF75ED792CFDC80ED630311D6ACDE7C60427603236CB1751EC
        ZIP  8439C217D3FC8E30E5259D94C54EEB7D05B0B18FE30426A3B0BFE3ACB1DC1D43
        ```
        """;

    [Fact]
    public void FindsTheMsiHashInRealReleaseNotes()
    {
        var sha = WithBody(RealNotes).FindSha256("MobreadModernDock-1.1.0-x64.msi");
        Assert.Equal("8dc666b82bc153df75ed792cfdc80ed630311d6acde7c60427603236cb1751ec", sha);
    }

    [Fact]
    public void PicksTheZipHashForAZipAsset()
    {
        // The two hashes sit on adjacent lines; taking the wrong one would
        // fail every verification for one artifact type.
        var sha = WithBody(RealNotes).FindSha256("MobreadModernDock-1.1.0-x64-portable.zip");
        Assert.Equal("8439c217d3fc8e30e5259d94c54eeb7d05b0b18fe30426a3b0bfe3acb1dc1d43", sha);
    }

    [Fact]
    public void ReturnsNullWhenNotesCarryNoHash()
    {
        // Older releases have no checksums; the caller must be able to tell
        // "no hash published" from "hash mismatch".
        Assert.Null(WithBody("Just some notes, nothing to verify.").FindSha256("x.msi"));
        Assert.Null(WithBody("").FindSha256("x.msi"));
    }

    [Fact]
    public void IgnoresHexThatIsNotTheRightLength()
    {
        // A short commit SHA must never be mistaken for a checksum.
        var sha = WithBody("Built from commit 8dc666b82bc153df").FindSha256("x.msi");
        Assert.Null(sha);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void ParsesReleaseTags(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("Version-1.0")]   // a tag style we don't publish
    public void RejectsUnparseableTags(string tag)
        => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void UpdateAvailableOnlyWhenNewer()
    {
        var cur = new Version(1, 0, 0);
        Assert.True(new UpdateChecker.Result(cur, new Version(1, 0, 1), null, null).UpdateAvailable);
        Assert.False(new UpdateChecker.Result(cur, new Version(1, 0, 0), null, null).UpdateAvailable);
        Assert.False(new UpdateChecker.Result(cur, new Version(0, 9, 9), null, null).UpdateAvailable);
        // A failed parse must not be read as "up to date" *or* as an update.
        Assert.False(new UpdateChecker.Result(cur, null, null, null).UpdateAvailable);
    }

    // --- startup check throttle ---

    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }

    private static (DockAppearanceService Svc, DockModel Model) Build()
    {
        var model = new DockModel();
        return (new DockAppearanceService(new DockService(new Repo(model))), model);
    }

    [Fact]
    public void ChecksOnFirstRun()
    {
        var (svc, _) = Build();
        Assert.True(svc.ShouldCheckForUpdates(DateTime.UtcNow));
    }

    [Fact]
    public void DoesNotRecheckWithinADay()
    {
        var (svc, _) = Build();
        var now = DateTime.UtcNow;
        svc.MarkUpdateChecked(now);

        // A restart loop must not hammer the API (60 req/h unauthenticated).
        Assert.False(svc.ShouldCheckForUpdates(now.AddMinutes(1)));
        Assert.False(svc.ShouldCheckForUpdates(now.AddHours(23)));
        Assert.True(svc.ShouldCheckForUpdates(now.AddHours(25)));
    }

    [Fact]
    public void NeverChecksWhenTheSettingIsOff()
    {
        var (svc, _) = Build();
        svc.SetCheckUpdatesOnStartup(false);
        Assert.False(svc.ShouldCheckForUpdates(DateTime.UtcNow));
    }

    [Fact]
    public void StartupCheckIsOnByDefault()
    {
        var (svc, _) = Build();
        Assert.True(svc.GetCheckUpdatesOnStartup());
    }

    [Fact]
    public void SurvivesConfigImport()
    {
        var target = new DockModel();
        var stamp = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var imported = new DockModel { CheckUpdatesOnStartup = false, LastUpdateCheckUtc = stamp };
        imported.Items.Add(new DockSettingsItemModel());

        target.CopyFrom(imported);

        Assert.False(target.CheckUpdatesOnStartup);
        Assert.Equal(stamp, target.LastUpdateCheckUtc);
    }
}
