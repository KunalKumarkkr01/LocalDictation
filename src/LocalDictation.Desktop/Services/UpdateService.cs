using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Checks GitHub Releases for a newer Velopack build and applies it in the background, then restarts.
/// Best-effort: it no-ops cleanly on a plain dev build (not a Velopack install), when offline, or when
/// no release has been published yet, so it is always safe to fire-and-forget at startup. See ADR-0014.
/// </summary>
public static class UpdateService
{
    private const string RepoUrl = "https://github.com/KunalKumarkkr01/LocalDictation";

    /// <summary>
    /// Runs one best-effort update check. Downloads and applies a newer release if found (which
    /// restarts the app); otherwise returns quietly. Never throws.
    /// </summary>
    public static async Task CheckAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!mgr.IsInstalled) return;                 // dev build — nothing to update against
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion is null) return;               // already on the latest release
            await mgr.DownloadUpdatesAsync(newVersion);
            mgr.ApplyUpdatesAndRestart(newVersion);       // exits and relaunches the new version
        }
        catch (Exception ex)
        {
            StartupLog.Write("Update check failed: " + ex.Message);
        }
    }
}
