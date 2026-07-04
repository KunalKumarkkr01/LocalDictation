namespace LocalDictation.Infrastructure;

/// <summary>
/// Resolves the on-disk locations the app uses for settings, history, models and plugins.
/// </summary>
/// <remarks>
/// Everything lives under <c>%AppData%/LocalDictation</c> by default. The models directory
/// additionally probes a repo-relative <c>models/whisper</c> folder and the
/// <c>LOCALDICTATION_MODELS</c> environment variable, so a developer's downloaded models are
/// found without copying them into AppData.
/// </remarks>
public sealed class AppPaths
{
    /// <summary>Root data directory.</summary>
    public string Root { get; }
    /// <summary>settings.json path.</summary>
    public string SettingsFile { get; }
    /// <summary>history.db path.</summary>
    public string HistoryDb { get; }
    /// <summary>Whisper models directory.</summary>
    public string ModelsDir { get; }
    /// <summary>Plugins directory.</summary>
    public string PluginsDir { get; }

    /// <summary>Creates paths, optionally overriding the models directory.</summary>
    public AppPaths(string? root = null, string? modelsDir = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalDictation");
        Directory.CreateDirectory(Root);
        SettingsFile = Path.Combine(Root, "settings.json");
        HistoryDb = Path.Combine(Root, "history.db");
        PluginsDir = Path.Combine(Root, "plugins");
        ModelsDir = modelsDir ?? ResolveModelsDir(Root);
    }

    private static string ResolveModelsDir(string root)
    {
        var env = Environment.GetEnvironmentVariable("LOCALDICTATION_MODELS");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        // Probe upwards from the executable for a repo-level models/whisper folder.
        // Trim any trailing separator so GetParent climbs one real level per iteration.
        var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "models", "whisper");
            if (Directory.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(root, "models", "whisper");
    }
}
