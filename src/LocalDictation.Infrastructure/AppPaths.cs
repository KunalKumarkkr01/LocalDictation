namespace LocalDictation.Infrastructure;

/// <summary>
/// Resolves the on-disk locations the app uses for settings, history, models and plugins.
/// </summary>
/// <remarks>
/// Everything lives under <c>%LocalAppData%/LocalDictation</c> on Windows (deliberately the same folder
/// Velopack installs into: settings, history, logs and the (large) Whisper models sit as siblings of the
/// versioned <c>current\</c> app directory, which Velopack wipes and re-diffs on every update — so
/// keeping data out of it preserves downloads across updates and keeps release packages small
/// (ADR-0014); local, not roaming, app-data also stops the multi-hundred-MB models from syncing in
/// enterprise roaming profiles) or <c>~/Library/Application Support/LocalDictation</c> on macOS
/// (<see cref="Environment.SpecialFolder.LocalApplicationData"/> resolves to the XDG
/// <c>~/.local/share</c> on macOS, not the Mac-native Application Support folder, so this is resolved
/// explicitly). The models directory additionally honors the <c>LOCALDICTATION_MODELS</c> environment
/// variable and probes a repo-relative <c>models/whisper</c> folder, so a developer's downloaded models
/// are found without copying them.
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
        Root = root ?? Path.Combine(ResolveDataRoot(), "LocalDictation");
        Directory.CreateDirectory(Root);
        SettingsFile = Path.Combine(Root, "settings.json");
        HistoryDb = Path.Combine(Root, "history.db");
        PluginsDir = Path.Combine(Root, "plugins");
        ModelsDir = modelsDir ?? ResolveModelsDir(Root);
    }

    /// <summary>Per-user app-data root: Mac-native Application Support, or Windows local app-data.</summary>
    private static string ResolveDataRoot() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

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
