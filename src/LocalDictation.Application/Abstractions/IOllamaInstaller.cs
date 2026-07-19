namespace LocalDictation.Application.Abstractions;

/// <summary>
/// Detects and, where possible, installs the Ollama backend. Platform-specific: Windows can silently
/// download and run the official installer; macOS has no unattended install path, so it only detects.
/// </summary>
public interface IOllamaInstaller
{
    /// <summary>Whether the Ollama executable is present (per-user install path or on PATH).</summary>
    bool IsInstalled();

    /// <summary>
    /// Attempts to install Ollama unattended. Returns whether Ollama ended up installed — on platforms
    /// with no silent-install path, this may simply return <see cref="IsInstalled"/> unchanged.
    /// </summary>
    Task<bool> EnsureInstalledAsync(CancellationToken ct = default);
}
