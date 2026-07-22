using LocalDictation.Domain;

namespace LocalDictation.Application.Abstractions;

/// <summary>Inspects the foreground window and focused control at trigger time.</summary>
public interface IWindowInspector
{
    /// <summary>
    /// Captures a <see cref="TargetControl"/> snapshot of whatever currently has focus.
    /// Must be called before showing the overlay so focus is not disturbed.
    /// </summary>
    TargetControl CaptureFocusedTarget();
}

/// <summary>Which registered hotkey fired.</summary>
public enum HotkeyAction { Primary, Picker }

/// <summary>Fired when a registered global hotkey is pressed.</summary>
public sealed class HotkeyPressedEventArgs : EventArgs
{
    /// <summary>Which hotkey fired. Primary starts/stops dictation; Picker opens the persona palette.</summary>
    public HotkeyAction Action { get; init; } = HotkeyAction.Primary;
}

/// <summary>Registers and surfaces the system-wide activation hotkey.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised (on the UI thread) when the activation hotkey is pressed.</summary>
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>Registers <paramref name="gesture"/> (e.g. "Ctrl+Win+Space").</summary>
    /// <returns>True if registered; false if the combination was unavailable.</returns>
    bool Register(string gesture);

    /// <summary>Removes the current registration.</summary>
    void Unregister();

    /// <summary>Registers the secondary persona-picker hotkey. Returns false if unavailable.</summary>
    bool RegisterPicker(string gesture);

    /// <summary>Removes the picker-hotkey registration.</summary>
    void UnregisterPicker();
}

/// <summary>Captures microphone audio and produces a normalised <see cref="AudioClip"/>.</summary>
public interface IAudioCaptureService : IDisposable
{
    /// <summary>Raised as capture progresses, carrying the current input level (0..1) for the meter.</summary>
    event EventHandler<double>? LevelChanged;

    /// <summary>Raised each buffer with per-band frequency magnitudes (0..1) for a spectrum-reactive meter.</summary>
    event EventHandler<float[]>? SpectrumChanged;

    /// <summary>Raised when voice-activity detection auto-stops on trailing silence.</summary>
    event EventHandler? SilenceDetected;

    /// <summary>Enumerates available capture device names.</summary>
    IReadOnlyList<string> GetInputDevices();

    /// <summary>True when the selected (or default) capture device is muted at the Windows level.
    /// A muted mic yields all-zero audio, so the overlay surfaces this. False on any query error.</summary>
    bool IsInputMuted();

    /// <summary>Begins capture from the configured/selected device.</summary>
    void Start();

    /// <summary>Stops capture and returns the buffered, resampled clip.</summary>
    AudioClip Stop();

    /// <summary>Stops capture and discards the buffer (cancel path).</summary>
    void Cancel();
}
