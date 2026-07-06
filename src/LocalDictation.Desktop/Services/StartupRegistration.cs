using System.IO;
using Microsoft.Win32;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Registers or removes the app from Windows startup via the per-user Run key. Prefers the stable
/// Velopack launcher stub (<c>%LocalAppData%\LocalDictation\LocalDictation.exe</c>), which forwards to
/// whatever version is current, over the version-specific <c>current\</c> path or a dev build output.
/// App boot re-applies this (see <c>App.xaml.cs</c>), so a stale entry left by a dev run self-heals on
/// the next launch of the installed app (ADR-0014).
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LocalDictation";

    /// <summary>Applies the desired start-with-Windows state.</summary>
    /// <param name="enabled">True to register the app to start at login; false to remove it.</param>
    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = ResolveLauncherPath();
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* startup registration is best-effort */ }
    }

    /// <summary>
    /// The exe to launch at login. For a Velopack install (<c>…\current\App.exe</c> with a sibling
    /// <c>…\App.exe</c> stub + <c>Update.exe</c>) this is the stable root stub, so the entry stays
    /// valid across updates. Otherwise the running exe (dev builds), as before.
    /// </summary>
    /// <returns>Absolute path to the launcher exe, or null when it can't be determined.</returns>
    private static string? ResolveLauncherPath()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return null;

        var dir = Path.GetDirectoryName(current);
        if (dir is not null &&
            string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(dir);
            if (root is not null)
            {
                var stub = Path.Combine(root, Path.GetFileName(current));
                if (File.Exists(stub) && File.Exists(Path.Combine(root, "Update.exe")))
                    return stub; // stable Velopack launcher
            }
        }
        return current; // dev / non-Velopack layout
    }
}
