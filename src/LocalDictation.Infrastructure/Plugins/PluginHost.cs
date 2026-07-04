using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using LocalDictation.Application.Abstractions;
using LocalDictation.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Plugins;

/// <summary>
/// Discovers, validates, isolates and activates plugins from the plugins directory
/// (design §9.2). Each plugin loads into its own collectible <see cref="AssemblyLoadContext"/>
/// so dependencies stay private and the plugin can be cleanly unloaded.
/// </summary>
public sealed class PluginHost : IDisposable
{
    private readonly string _pluginsDir;
    private readonly ILogger<PluginHost> _log;
    private readonly List<LoadedPlugin> _loaded = new();

    /// <summary>Text processors contributed by activated plugins.</summary>
    public IReadOnlyList<ITextProcessor> ContributedProcessors =>
        _loaded.SelectMany(l => l.Context.Processors).ToList();

    /// <summary>Output targets contributed by activated plugins.</summary>
    public IReadOnlyList<IOutputTarget> ContributedTargets =>
        _loaded.SelectMany(l => l.Context.Targets).ToList();

    /// <summary>Creates the host rooted at <paramref name="pluginsDirectory"/>.</summary>
    public PluginHost(string pluginsDirectory, ILogger<PluginHost> log)
    {
        _pluginsDir = pluginsDirectory;
        _log = log;
        Directory.CreateDirectory(_pluginsDir);
    }

    /// <summary>Discovers and activates every valid plugin under the plugins directory.</summary>
    public void LoadAll()
    {
        foreach (var dir in Directory.EnumerateDirectories(_pluginsDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;
            try { LoadOne(dir, manifestPath); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to load plugin in {Dir}", dir); }
        }
    }

    private void LoadOne(string dir, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Entry))
        {
            _log.LogWarning("Invalid manifest in {Dir}", dir);
            return;
        }

        if (!IsHostCompatible(manifest.MinHostVersion))
        {
            _log.LogWarning("Plugin {Id} requires host {Min}; skipping.", manifest.Id, manifest.MinHostVersion);
            return;
        }

        var assemblyPath = Path.Combine(dir, manifest.Entry);
        var alc = new PluginLoadContext(assemblyPath);
        var asm = alc.LoadFromAssemblyPath(assemblyPath);

        var pluginType = asm.GetTypes().FirstOrDefault(t =>
            typeof(IDictationPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        if (pluginType is null)
        {
            _log.LogWarning("Plugin {Id} has no IDictationPlugin implementation.", manifest.Id);
            alc.Unload();
            return;
        }

        var instance = (IDictationPlugin)Activator.CreateInstance(pluginType)!;
        var context = new PluginContext(manifest, _log);
        instance.OnActivate(context);
        _loaded.Add(new LoadedPlugin(instance, alc, context));
        _log.LogInformation("Activated plugin {Id} v{Ver} ({Proc} processors, {Tgt} targets).",
            manifest.Id, manifest.Version, context.Processors.Count, context.Targets.Count);
    }

    private static bool IsHostCompatible(string minVersion)
    {
        var host = typeof(PluginHost).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return Version.TryParse(minVersion, out var min) ? host >= min : true;
    }

    /// <summary>Deactivates and unloads all plugins.</summary>
    public void Dispose()
    {
        foreach (var l in _loaded)
        {
            try { l.Instance.OnDeactivate(); } catch (Exception ex) { _log.LogWarning(ex, "Plugin deactivate failed."); }
            l.Context.Clear();
            l.Alc.Unload();
        }
        _loaded.Clear();
    }

    private sealed record LoadedPlugin(IDictationPlugin Instance, PluginLoadContext Alc, PluginContext Context);
}

/// <summary>Collectible load context that resolves a plugin's private dependencies.</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string mainAssemblyPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null; // fall back to host/shared context
    }
}

/// <summary>Scoped context handed to a plugin, collecting its registered contributions.</summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly ILogger _log;
    public List<ITextProcessor> Processors { get; } = new();
    public List<IOutputTarget> Targets { get; } = new();

    public PluginContext(PluginManifest manifest, ILogger log)
    {
        Manifest = manifest;
        _log = log;
    }

    public PluginManifest Manifest { get; }
    public void Log(string message) => _log.LogInformation("[plugin:{Id}] {Message}", Manifest.Id, message);
    public void RegisterTextProcessor(ITextProcessor processor) => Processors.Add(processor);
    public void RegisterOutputTarget(IOutputTarget target) => Targets.Add(target);
    public void Clear() { Processors.Clear(); Targets.Clear(); }
}
