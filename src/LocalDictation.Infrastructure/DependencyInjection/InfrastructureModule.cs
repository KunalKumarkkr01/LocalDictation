using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Application.Processing;
using LocalDictation.Infrastructure.Ai;
using LocalDictation.Infrastructure.Diagnostics;
using LocalDictation.Infrastructure.Persistence;
using LocalDictation.Infrastructure.Plugins;
using LocalDictation.Infrastructure.Speech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the portable (cross-platform) Infrastructure adapters behind their Application ports.
/// </summary>
/// <remarks>
/// This is the single seam where portable engines are bound to interfaces — swapping Whisper,
/// the LLM provider or persistence is a one-line change here. Platform-specific adapters (audio
/// capture, hotkey, window inspection, text insertion, self-test) are registered separately by the
/// per-OS modules: <c>AddWindowsInfrastructure()</c> (LocalDictation.Infrastructure.Windows) or
/// <c>AddMacInfrastructure()</c> (LocalDictation.Infrastructure.Mac). The Desktop host layers its UI
/// services (overlay, editor, dispatcher) on top.
/// </remarks>
public static class InfrastructureModule
{
    /// <summary>
    /// Registers the cross-platform infrastructure services. Requires an <see cref="AppSettings"/>
    /// and <see cref="AppPaths"/> already registered. Call a platform module afterwards
    /// (<c>AddWindowsInfrastructure()</c> / <c>AddMacInfrastructure()</c>) to bind the OS adapters,
    /// plus the Desktop-provided UI ports (<see cref="IUiDispatcher"/>, <see cref="IFloatingEditor"/>,
    /// <see cref="IOverlayController"/>).
    /// </summary>
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
    {
        // ---- HTTP clients ----
        services.AddHttpClient();

        // ---- Speech ----
        services.AddSingleton<ISpeechModelManager>(sp => new SpeechModelManager(
            sp.GetRequiredService<AppPaths>().ModelsDir,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("models"),
            sp.GetRequiredService<ILogger<SpeechModelManager>>()));
        services.AddSingleton<ISpeechEngine, WhisperNetEngine>();

        // ---- AI text processing (Ollama; pipeline degrades to raw text if unavailable) ----
        services.AddSingleton<ITextProcessor>(sp => new OllamaTextProcessor(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<ILogger<OllamaTextProcessor>>()));
        services.AddSingleton<IOllamaLifecycle>(sp => new OllamaLifecycle(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama-lifecycle"),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<IOllamaInstaller>(),
            sp.GetRequiredService<ILogger<OllamaLifecycle>>()));

        // ---- Persistence ----
        services.AddSingleton<IHistoryRepository>(sp => new SqliteHistoryRepository(
            sp.GetRequiredService<AppPaths>().HistoryDb,
            sp.GetRequiredService<ILogger<SqliteHistoryRepository>>()));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            sp.GetRequiredService<AppPaths>().SettingsFile,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // ---- Plugins ----
        services.AddSingleton(sp => new PluginHost(
            sp.GetRequiredService<AppPaths>().PluginsDir,
            sp.GetRequiredService<ILogger<PluginHost>>()));

        // ---- Diagnostics ----
        services.AddSingleton<IReadinessService, ReadinessService>();

        // ---- Personas ----
        services.AddSingleton<IPersonaResolver, PersonaResolver>();

        // ---- Orchestration ----
        services.AddSingleton<DictationPipeline>();

        return services;
    }
}
