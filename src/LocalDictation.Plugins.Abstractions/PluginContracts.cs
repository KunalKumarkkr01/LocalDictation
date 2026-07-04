using LocalDictation.Application.Abstractions;

namespace LocalDictation.Plugins.Abstractions;

/// <summary>
/// Manifest metadata declared by a plugin in its <c>plugin.json</c> and validated by the host.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Unique plugin id (reverse-DNS recommended).</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Semantic version of the plugin.</summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>Assembly file name that contains the entry point.</summary>
    public string Entry { get; set; } = string.Empty;
    /// <summary>Minimum host version this plugin supports.</summary>
    public string MinHostVersion { get; set; } = "1.0.0";
    /// <summary>Declared capabilities (e.g. "text-processor", "output-target", "network").</summary>
    public List<string> Capabilities { get; set; } = new();
}

/// <summary>
/// Scoped services handed to a plugin at activation. Deliberately narrow — no ambient
/// network or filesystem beyond what the plugin's capabilities grant.
/// </summary>
public interface IPluginContext
{
    /// <summary>The plugin's own manifest.</summary>
    PluginManifest Manifest { get; }

    /// <summary>Writes a diagnostic line to the host log, namespaced to the plugin.</summary>
    void Log(string message);

    /// <summary>Registers a text processor contributed by the plugin.</summary>
    void RegisterTextProcessor(ITextProcessor processor);

    /// <summary>Registers an output target contributed by the plugin.</summary>
    void RegisterOutputTarget(IOutputTarget target);
}

/// <summary>
/// Entry point every plugin implements. The host discovers, isolates (per collectible
/// <c>AssemblyLoadContext</c>) and drives it through this lifecycle.
/// </summary>
public interface IDictationPlugin
{
    /// <summary>Called once after load; register contributions here.</summary>
    void OnActivate(IPluginContext context);

    /// <summary>Called before unload; release resources here.</summary>
    void OnDeactivate();
}
