namespace MobreadModernDock.Core.Application;

using System.Net.Http;
using System.Text.Json;

/// <summary>
/// #16 Checks GitHub Releases for a newer version. No auth, one request,
/// swallowed failures — this must never affect startup.
/// </summary>
public sealed class UpdateChecker
{
    public const string Repo = "mobread/MobreadModernDock";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";
    private const string ApiUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MobreadModernDock");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public sealed record Result(Version Current, Version? Latest, string? DownloadUrl, string? ReleaseUrl)
    {
        public bool UpdateAvailable => Latest != null && Latest > Current;
    }

    /// <summary>Returns null when the check couldn't complete (offline, rate-limited, no releases yet).</summary>
    public static async Task<Result?> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(ApiUrl, ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            var latest = ParseVersion(tag);
            if (latest == null) return null;

            string? msi = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    { msi = a.GetProperty("browser_download_url").GetString(); break; }
                }
            return new Result(current, latest, msi, root.TryGetProperty("html_url", out var h) ? h.GetString() : ReleasesPage);
        }
        catch { return null; }
    }

    /// <summary>"v1.3.0", "1.3", "1.3.0-beta" → 1.3.0. Null when unparseable.</summary>
    public static Version? ParseVersion(string tag)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];
        var parts = s.Split('.');
        if (parts.Length == 2) s += ".0";
        return Version.TryParse(s, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }
}
