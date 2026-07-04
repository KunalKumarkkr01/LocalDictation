namespace LocalDictation.Domain;

/// <summary>
/// A snapshot of the window and focused control that were active when the user
/// triggered dictation. Drives overlay labels, blocklist checks and insertion routing.
/// </summary>
/// <remarks>
/// Captured by <c>IWindowInspector</c> the instant the hotkey fires, before any
/// overlay is shown (so the overlay does not steal focus and corrupt the snapshot).
/// </remarks>
public sealed class TargetControl
{
    /// <summary>Native handle of the foreground window.</summary>
    public nint WindowHandle { get; init; }

    /// <summary>Process name of the foreground window (e.g. "chrome", "Teams").</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Foreground window caption.</summary>
    public string WindowTitle { get; init; } = string.Empty;

    /// <summary>Human-friendly control label (e.g. "Chat input", "Address bar").</summary>
    public string ControlName { get; init; } = string.Empty;

    /// <summary>Classification of the focused control.</summary>
    public ControlKind Kind { get; init; } = ControlKind.Unknown;

    /// <summary>Whether text can be inserted into the focused control.</summary>
    public bool IsEditable { get; init; }

    /// <summary>Whether the control is a password / sensitive field (dictation blocked).</summary>
    public bool IsSensitive { get; init; }

    /// <summary>Whether the foreground window belongs to an elevated (admin) process.</summary>
    public bool IsElevated { get; init; }

    /// <summary>A sentinel target used when no useful window could be inspected.</summary>
    public static TargetControl Unknown { get; } = new()
    {
        ProcessName = "unknown",
        Kind = ControlKind.Unsupported,
        IsEditable = false
    };

    /// <summary>Short "App › Control" descriptor for the overlay.</summary>
    public string Descriptor =>
        string.IsNullOrWhiteSpace(ControlName) ? WindowTitle : $"{WindowTitle} › {ControlName}";
}
