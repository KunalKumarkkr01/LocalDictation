using LocalDictation.Application.Abstractions;
using LocalDictation.Infrastructure.Audio;
using LocalDictation.Infrastructure.Diagnostics;
using LocalDictation.Infrastructure.Windows;
using LocalDictation.Infrastructure.Windows.Ai;
using LocalDictation.Infrastructure.Windows.Output;
using Microsoft.Extensions.DependencyInjection;

namespace LocalDictation.Infrastructure.Windows.DependencyInjection;

/// <summary>
/// Registers the Windows-only Infrastructure adapters behind their Application ports.
/// </summary>
/// <remarks>
/// Call after <c>AddCoreInfrastructure()</c>. Binds the OS integration surface — mic capture,
/// global hotkey, focused-window inspection, text insertion strategies and the TTS self-test —
/// to their Win32/WPF/NAudio/System.Speech implementations. The macOS equivalent is
/// <c>AddMacInfrastructure()</c> in LocalDictation.Infrastructure.Mac.
/// </remarks>
public static class WindowsInfrastructureModule
{
    /// <summary>Registers the Windows platform adapters. Requires <c>AddCoreInfrastructure()</c> first.</summary>
    public static IServiceCollection AddWindowsInfrastructure(this IServiceCollection services)
    {
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

        // ---- Diagnostics ----
        services.AddSingleton<IDictationSelfTest, TtsDictationSelfTest>();

        // ---- AI (Ollama install detection) ----
        services.AddSingleton<IOllamaInstaller, WindowsOllamaInstaller>();

        return services;
    }
}
