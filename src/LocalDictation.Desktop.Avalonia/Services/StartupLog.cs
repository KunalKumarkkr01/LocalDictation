namespace LocalDictation.Services;

/// <summary>
/// Minimal always-on file logger for startup + crash diagnostics, written to
/// <c>&lt;AppPaths.Root&gt;/startup.log</c>. Independent of the DI logging pipeline so it captures
/// failures that happen before (or instead of) normal logging. Port of the Windows StartupLog.
/// </summary>
public static class StartupLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalDictation", "startup.log");

    /// <summary>Appends a timestamped line to the log (best-effort; never throws).</summary>
    /// <param name="message">The line to record.</param>
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Starts a fresh log for this launch.</summary>
    public static void Reset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"=== LocalDictation launch {DateTime.Now:u} ==={Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}
