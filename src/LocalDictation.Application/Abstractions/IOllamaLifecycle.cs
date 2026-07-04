namespace LocalDictation.Application.Abstractions;

/// <summary>Lifecycle state of the local LLM backend, surfaced to the control panel.</summary>
public enum OllamaStatus
{
    /// <summary>AI enhancement is off; no model resident.</summary>
    Off,
    /// <summary>The Ollama service is being started.</summary>
    Starting,
    /// <summary>The model is being loaded into memory.</summary>
    LoadingModel,
    /// <summary>Ready to enhance text.</summary>
    Ready,
    /// <summary>Could not start Ollama or load the model.</summary>
    Failed
}

/// <summary>
/// Owns the local LLM backend behind the AI Enhancement toggle: starts the Ollama service and
/// loads the model in the background when enabled, releases it when disabled. The user never
/// touches a terminal.
/// </summary>
public interface IOllamaLifecycle
{
    /// <summary>Current lifecycle status.</summary>
    OllamaStatus Status { get; }

    /// <summary>Raised (on a background thread) whenever <see cref="Status"/> changes, with a message.</summary>
    event EventHandler<(OllamaStatus Status, string Message)>? StatusChanged;

    /// <summary>
    /// Ensures Ollama is running and <paramref name="model"/> is loaded, starting the service if
    /// needed. Returns true when Ready.
    /// </summary>
    Task<bool> EnableAsync(string model, CancellationToken ct = default);

    /// <summary>Releases the resident model (lets it unload) and marks the backend Off.</summary>
    Task DisableAsync(string model, CancellationToken ct = default);
}
