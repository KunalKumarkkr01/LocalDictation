using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Infrastructure.Ai;
using LocalDictation.Infrastructure.Audio;
using LocalDictation.Infrastructure.Persistence;
using LocalDictation.Infrastructure.Plugins;
using LocalDictation.Infrastructure.Speech;
using LocalDictation.Infrastructure.Windows;
using LocalDictation.Infrastructure.Windows.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.DependencyInjection;

/// <summary>
/// Composition helpers that register every Infrastructure adapter behind its Application port.
/// </summary>
/// <remarks>
/// This is the single seam where concrete engines are bound to interfaces — swapping Whisper,
/// the LLM provider or an insertion strategy is a one-line change here (design principle:
/// extensibility). The Desktop host layers its UI services (overlay, editor, dispatcher) on top.
/// </remarks>
public static class InfrastructureModule
{
    /// <summary>
    /// Registers infrastructure services. Requires an <see cref="AppSettings"/> and
    /// <see cref="AppPaths"/> already registered, plus the Desktop-provided UI ports
    /// (<see cref="IUiDispatcher"/>, <see cref="IFloatingEditor"/>, <see cref="IOverlayController"/>).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
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

        // ---- Audio ----
        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();

        // ---- Windows integration ----
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IWindowInspector, Win32Inspector>();

        // ---- Output / insertion ----
        services.AddSingleton<IOutputTarget, ClipboardOutputTarget>();
        services.AddSingleton<IOutputTarget, SendInputOutputTarget>();
        services.AddSingleton<IOutputTarget, UiaOutputTarget>();
        services.AddSingleton<IOutputRouter, OutputRouter>();

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

        // ---- Orchestration ----
        services.AddSingleton<DictationPipeline>();

        return services;
    }
}
