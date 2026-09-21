namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Downloads a release MSI and hands it to Windows Installer.
///
/// The app cannot upgrade itself in place — its own files are locked while it
/// runs — so the flow is: download to temp, verify the hash, start msiexec,
/// then exit. `MajorUpgrade` in Installer.wxs makes msiexec replace the old
/// install and keep user data (which lives in %APPDATA%, not the install dir).
///
/// Portable installs are deliberately not handled here: there is no installer
/// to run and the exe cannot overwrite itself. Callers check
/// <see cref="AppDataLocator.IsPortable"/> and point those users at the ZIP.
/// </summary>
public static class UpdateInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // No overall timeout: a 50 MB download on a slow line is legitimate.
        // Cancellation is the caller's job via the token.
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MobreadModernDock");
        return c;
    }

    public sealed record DownloadResult(bool Ok, string? FilePath, string? Error)
    {
        public static DownloadResult Fail(string error) => new(false, null, error);
    }

    /// <summary>
    /// Fetches the MSI to a temp file, reporting 0..1 progress. When
    /// <paramref name="expectedSha256"/> is supplied the file is verified and
    /// deleted on mismatch — an unsigned installer is not something to run on
    /// trust.
    /// </summary>
    public static async Task<DownloadResult> DownloadAsync(
        string url, string? expectedSha256, long expectedSize,
        IProgress<double>? progress, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MobreadModernDockUpdate");
        string file = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));

        try
        {
            Directory.CreateDirectory(dir);
            // A half-finished file from a previous attempt must not be reused.
            if (File.Exists(file)) File.Delete(file);

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? expectedSize;

                await using var http = await response.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(file);

                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await http.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
                }
            }

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                string actual = await Sha256Async(file, ct);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                    return DownloadResult.Fail("checksum");
                }
            }

            return new DownloadResult(true, file, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(file);
            throw;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[UpdateInstaller] download failed: {e.Message}");
            TryDelete(file);
            return DownloadResult.Fail("download");
        }
    }

    /// <summary>
    /// Starts msiexec on the downloaded package and returns immediately. The
    /// caller must shut the app down right after, or the upgrade will fail on
    /// files this process still holds open.
    ///
    /// /passive shows a progress bar but asks nothing; a silent swap of an
    /// unsigned binary is exactly the behaviour that makes people suspicious.
    /// </summary>
    public static bool LaunchInstaller(string msiPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /passive /norestart",
                UseShellExecute = true,   // lets UAC prompt for elevation
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[UpdateInstaller] msiexec failed: {e.Message}");
            return false;
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
