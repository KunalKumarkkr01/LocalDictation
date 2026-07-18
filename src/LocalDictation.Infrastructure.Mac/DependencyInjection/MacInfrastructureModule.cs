using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Infrastructure.Mac.Ai;
using LocalDictation.Infrastructure.Mac.Audio;
using LocalDictation.Infrastructure.Mac.Diagnostics;
using LocalDictation.Infrastructure.Mac.Input;
using LocalDictation.Infrastructure.Mac.Output;
using Microsoft.Extensions.DependencyInjection;

namespace LocalDictation.Infrastructure.Mac.DependencyInjection;

/// <summary>
/// Registers the macOS-only Infrastructure adapters behind their Application ports.
/// </summary>
/// <remarks>
/// Call after <c>AddCoreInfrastructure()</c>. Binds the OS integration surface — CoreAudio mic
/// capture, Carbon global hotkey, Accessibility focused-window inspection, CGEvent/pasteboard/AX text
/// insertion and the <c>say</c> self-test — to their macOS implementations. The Windows equivalent is
/// <c>AddWindowsInfrastructure()</c> in LocalDictation.Infrastructure.Windows.
/// </remarks>
[SupportedOSPlatform("macos")]
public static class MacInfrastructureModule
{
    /// <summary>Registers the macOS platform adapters. Requires <c>AddCoreInfrastructure()</c> first.</summary>
    public static IServiceCollection AddMacInfrastructure(this IServiceCollection services)
    {
        // ---- Audio ----
        services.AddSingleton<IAudioCaptureService, CoreAudioCaptureService>();

        // ---- macOS integration ----
        services.AddSingleton<IHotkeyService, CarbonHotkeyService>();
        services.AddSingleton<IWindowInspector, AxWindowInspector>();

        // ---- Output / insertion ----
        services.AddSingleton<IOutputTarget, PasteboardOutputTarget>();
        services.AddSingleton<IOutputTarget, CgKeystrokeOutputTarget>();
        services.AddSingleton<IOutputTarget, AxValueOutputTarget>();
        services.AddSingleton<IOutputRouter, MacOutputRouter>();

        // ---- Diagnostics ----
        services.AddSingleton<IDictationSelfTest, SaySelfTest>();

        // ---- AI (Ollama install detection) ----
        services.AddSingleton<IOllamaInstaller, MacOllamaInstaller>();

        return services;
    }
}
