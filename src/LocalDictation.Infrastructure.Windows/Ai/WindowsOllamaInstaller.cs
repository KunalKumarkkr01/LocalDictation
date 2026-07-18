using System.Diagnostics;
using System.Linq;
using LocalDictation.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows.Ai;

/// <summary>
/// Detects and silently installs Ollama on Windows via the official downloadable installer.
/// </summary>
public sealed class WindowsOllamaInstaller : IOllamaInstaller
{
    private readonly HttpClient _http;
    private readonly ILogger<WindowsOllamaInstaller> _log;

    /// <summary>Creates the installer.</summary>
    public WindowsOllamaInstaller(HttpClient http, ILogger<WindowsOllamaInstaller> log)
    {
        _http = http;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsInstalled()
    {
        var perUser = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe");
        if (File.Exists(perUser)) return true;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator).Any(d =>
        {
            try { return !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d.Trim(), "ollama.exe")); }
            catch { return false; }
        });
    }

    /// <summary>
    /// Downloads and runs the official Ollama installer when it isn't present, then waits for the
    /// executable to appear. Best-effort; returns whether Ollama ended up installed.
    /// </summary>
    public async Task<bool> EnsureInstalledAsync(CancellationToken ct = default)
    {
        try
        {
            var installer = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            using (var resp = await _http.GetAsync("https://ollama.com/download/OllamaSetup.exe",
                HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(installer);
                await src.CopyToAsync(fs, ct);
            }

            var proc = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            if (proc is not null) await proc.WaitForExitAsync(ct);

            for (int i = 0; i < 30 && !IsInstalled(); i++) await Task.Delay(1000, ct);
            return IsInstalled();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ollama auto-install failed.");
            return false;
        }
    }
}
