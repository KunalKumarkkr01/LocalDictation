using Microsoft.Win32;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Registers or removes the app from Windows startup via the per-user Run key, pointing at
/// <see cref="Environment.ProcessPath"/>. Under a Velopack install this resolves to the stable
/// <c>%LocalAppData%\LocalDictation\current\LocalDictation.exe</c>, which survives updates, so the
/// key stays valid (ADR-0014). In a dev run it points at the build output, as before.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LocalDictation";

    /// <summary>Applies the desired start-with-Windows state.</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* startup registration is best-effort */ }
    }
}
