using LocalDictation.Domain;

namespace LocalDictation.Application.Abstractions;

/// <summary>Visual state the overlay can present during a session.</summary>
public enum OverlayStage { Recording, Transcribing, Processing, Error }

/// <summary>Controls the small, non-activating recording overlay.</summary>
public interface IOverlayController
{
    /// <summary>Shows the overlay for a target near its window; raises <see cref="Cancelled"/> on ESC.</summary>
    void Show(TargetControl target);

    /// <summary>Updates the overlay stage label / spinner.</summary>
    void SetStage(OverlayStage stage, string? message = null);

    /// <summary>Feeds the live input level (0..1) to the meter.</summary>
    void UpdateLevel(double level);

    /// <summary>Feeds per-band frequency magnitudes (0..1) so the waveform reacts to the voice spectrum.</summary>
    void UpdateSpectrum(float[] bands);

    /// <summary>Hides the overlay.</summary>
    void Hide();

    /// <summary>Raised when the user presses ESC (or the overlay cancel hotspot).</summary>
    event EventHandler? Cancelled;
}

/// <summary>Shows the floating editor when text cannot be inserted automatically.</summary>
public interface IFloatingEditor
{
    /// <summary>Displays <paramref name="text"/> for manual copy/edit/retry.</summary>
    void ShowFor(string text, TargetControl target);
}

/// <summary>Marshals work onto the UI (STA) thread — required for clipboard and window ops.</summary>
public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread and awaits completion.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Runs <paramref name="func"/> on the UI thread and returns its result.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>Posts <paramref name="action"/> to the UI thread without waiting.</summary>
    void Post(Action action);
}

/// <summary>Surfaces toast notifications for status and errors.</summary>
public interface INotificationService
{
    /// <summary>Shows an informational toast.</summary>
    void Info(string title, string message);

    /// <summary>Shows an error toast.</summary>
    void Error(string title, string message);
}
