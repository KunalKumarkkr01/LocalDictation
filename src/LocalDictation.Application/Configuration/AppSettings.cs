using LocalDictation.Domain;

namespace LocalDictation.Application.Configuration;

/// <summary>
/// Strongly-typed application settings, serialised to
/// <c>%AppData%/LocalDictation/settings.json</c>. Secrets are DPAPI-encrypted.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Schema version for forward-compatible migration.</summary>
    public int SchemaVersion { get; set; } = 1;

    // ---- Activation ----
    /// <summary>Global activation gesture. Avoids Win-key combos, which Windows reserves.</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+Space";

    // ---- Audio ----
    /// <summary>Selected input device name, or null for system default.</summary>
    public string? MicrophoneDevice { get; set; }

    /// <summary>
    /// Auto-stop recording after a silence. Off by default so the simple press-to-start /
    /// press-to-stop toggle never chops speech mid-sentence; opt in for hands-free stop.
    /// </summary>
    public bool AutoStopOnSilence { get; set; }

    /// <summary>Trailing silence (ms) before VAD auto-stops recording (when enabled).</summary>
    public int SilenceTimeoutMs { get; set; } = 2000;

    /// <summary>Enable optional noise suppression.</summary>
    public bool NoiseSuppression { get; set; }

    // ---- Speech ----
    /// <summary>Active Whisper model; null lets the app auto-select for the hardware.</summary>
    public SpeechModelSize? WhisperModel { get; set; }

    /// <summary>Recognition language ("auto" to detect).</summary>
    public string Language { get; set; } = "auto";

    // ---- AI ----
    /// <summary>
    /// Enable local LLM post-processing (grammar/rewrite/etc). Off by default for a snappy,
    /// seamless flow — raw Whisper output is already punctuated and capitalized. Turn on in
    /// Settings when you want AI cleanup; adds ~1-2 s of latency per dictation.
    /// </summary>
    public bool AiEnabled { get; set; }

    /// <summary>Ollama base URL.</summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>Default Ollama model tag.</summary>
    public string LlmModel { get; set; } = "phi3.5:3.8b-mini-instruct-q4_K_M";

    /// <summary>Default processing mode applied per dictation.</summary>
    public ProcessingMode DefaultMode { get; set; } = ProcessingMode.GrammarCorrection;

    /// <summary>Target language for translation mode.</summary>
    public string TranslationTarget { get; set; } = "en";

    /// <summary>User custom prompt for <see cref="ProcessingMode.Custom"/>.</summary>
    public string CustomPrompt { get; set; } = "";

    /// <summary>Keep the LLM resident between dictations for lower latency.</summary>
    public bool KeepModelResident { get; set; } = true;

    // ---- Output ----
    /// <summary>Preferred insertion strategy order, most-preferred first.</summary>
    public List<string> InsertionOrder { get; set; } = new() { "clipboard", "sendinput", "uia" };

    /// <summary>Never touch the clipboard (forces SendInput/UIA paths).</summary>
    public bool NeverUseClipboard { get; set; }

    /// <summary>
    /// When the originally targeted window is no longer focused at delivery time (you clicked away or
    /// alt-tabbed), open the floating editor instead of typing into whatever now has focus. On by
    /// default so dictated text is never misdirected into the wrong app.
    /// </summary>
    public bool EditorOnFocusLoss { get; set; } = true;

    // ---- Privacy ----
    /// <summary>Process names where dictation is always blocked.</summary>
    public List<string> BlockedApps { get; set; } = new() { "keepass", "1password", "bitwarden" };

    // ---- History ----
    /// <summary>Persist dictation history.</summary>
    public bool HistoryEnabled { get; set; } = true;

    /// <summary>
    /// Days to keep non-favorite history before it is pruned. Favorites (and pinned entries) are kept
    /// indefinitely regardless. <c>0 or less</c> means keep everything forever.
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 30;

    /// <summary>Encrypt history text at rest.</summary>
    public bool EncryptHistory { get; set; }

    // ---- App ----
    /// <summary>Start automatically with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Show a tray notification with the transcript after each completed dictation.</summary>
    public bool NotifyOnComplete { get; set; } = true;

    /// <summary>UI theme: "dark", "light" or "system".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Prefer GPU acceleration when available.</summary>
    public bool PreferGpu { get; set; }
}
