using System.Diagnostics;
using System.Runtime.Versioning;

namespace LocalDictation.Services;

/// <summary>
/// Registers or removes the app from macOS login items by writing a per-user LaunchAgent plist to
/// <c>~/Library/LaunchAgents/com.localdictation.app.plist</c>. The macOS equivalent of the Windows
/// per-user Run key (StartupRegistration). Best-effort; failures are swallowed.
/// </summary>
[SupportedOSPlatform("macos")]
public static class LaunchAgentRegistration
{
    private const string Label = "com.localdictation.app";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label}.plist");

    /// <summary>Applies the desired start-at-login state.</summary>
    /// <param name="enabled">True to register the app to launch at login; false to remove it.</param>
    public static void Apply(bool enabled)
    {
        try
        {
            if (enabled) Register();
            else Remove();
        }
        catch { /* start-at-login registration is best-effort */ }
    }

    private static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, BuildPlist(exe));

        // Ask launchd to load it now so it also takes effect this session (ignore if already loaded).
        RunLaunchctl("load", PlistPath);
    }

    private static void Remove()
    {
        if (!File.Exists(PlistPath)) return;
        RunLaunchctl("unload", PlistPath);
        File.Delete(PlistPath);
    }

    /// <summary>Builds a minimal RunAtLoad LaunchAgent plist for the given executable.</summary>
    private static string BuildPlist(string exePath) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{Label}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{exePath}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
            <key>ProcessType</key>
            <string>Interactive</string>
        </dict>
        </plist>
        """;

    private static void RunLaunchctl(string verb, string plistPath)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("/bin/launchctl", $"{verb} \"{plistPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            p?.WaitForExit(3000);
        }
        catch { /* launchd may reject a duplicate load/unload — non-fatal */ }
    }
}
