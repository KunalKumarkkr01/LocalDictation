using System.Linq;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Infrastructure.Mac.Ai;

/// <summary>
/// Detects Ollama on macOS. Unlike Windows, there's no unattended silent-install path — Ollama ships as
/// a drag-install <c>.app</c> or via <c>brew install ollama</c> — so this only detects; the caller's
/// existing failure message already points the user to ollama.com.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOllamaInstaller : IOllamaInstaller
{
    /// <inheritdoc />
    public bool IsInstalled() =>
        File.Exists("/opt/homebrew/bin/ollama") || File.Exists("/usr/local/bin/ollama") ||
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Any(d => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d.Trim(), "ollama")));

    /// <inheritdoc />
    public Task<bool> EnsureInstalledAsync(CancellationToken ct = default) => Task.FromResult(false);
}
