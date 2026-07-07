using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Views;

namespace LocalDictation.Services;

/// <summary>
/// Drives the recording capsule: shows/positions it, streams the mic level and spectrum, switches
/// stage visuals, polls the mute state, and raises <see cref="Cancelled"/> on ESC. Avalonia port of
/// the WPF OverlayController.
/// </summary>
/// <remarks>
/// NOTE: The WPF version registers a scoped global ESC hotkey (the overlay never takes focus). A true
/// system-wide ESC on macOS needs a CGEventTap; here ESC is handled via the window's own key handler
/// (and a click on the capsule also cancels) — a clean functional equivalent for the UI shell.
/// </remarks>
public sealed class AvaloniaOverlayController : IOverlayController
{
    private readonly IUiDispatcher _ui;
    private readonly IAudioCaptureService _capture;
    private OverlayWindow? _window;
    private DispatcherTimer? _muteTimer;
    private IImage? _appMark;

    /// <inheritdoc />
    public event EventHandler? Cancelled;

    /// <summary>Creates the controller.</summary>
    public AvaloniaOverlayController(IUiDispatcher ui, IAudioCaptureService capture)
    {
        _ui = ui;
        _capture = capture;
    }

    /// <inheritdoc />
    public void Show(TargetControl target) => _ui.Post(() =>
    {
        EnsureWindow();
        _window!.SetMode(false, Brush("RecordingBrush"));
        _window.SetTarget(DescribeTarget(target));
        _window.SetTargetIcon(_appMark);
        _window.SetLevel(0);
        _window.SetMicMuted(SafeIsMuted());
        _window.Show();
        _window.PositionBottomCenter();
        StartMutePolling();
    });

    /// <inheritdoc />
    public void SetStage(OverlayStage stage, string? message = null) => _ui.Post(() =>
    {
        if (_window is null) return;
        switch (stage)
        {
            case OverlayStage.Recording:
                _window.SetMode(false, Brush("RecordingBrush"));
                break;
            case OverlayStage.Transcribing:
            case OverlayStage.Processing:
                _window.SetMode(true, Brush("ProcessingBrush"));
                break;
            default:
                _window.SetMode(false, Brush("DangerBrush"));
                break;
        }
    });

    /// <inheritdoc />
    public void UpdateLevel(double level) => _ui.Post(() => _window?.SetLevel(level));

    /// <inheritdoc />
    public void UpdateSpectrum(float[] bands) => _ui.Post(() => _window?.SetSpectrum(bands));

    /// <inheritdoc />
    public void Hide() => _ui.Post(() =>
    {
        StopMutePolling();
        _window?.Hide();
    });

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new OverlayWindow();
        _appMark = Avalonia.Application.Current is { } app && app.TryGetResource("AppMark", null, out var v) ? v as IImage : null;

        // ESC cancels; clicking the capsule also cancels (a friendly fallback where global ESC isn't wired).
        _window.KeyDown += (_, e) => { if (e.Key == Key.Escape) RaiseCancelled(); };
        _window.PointerPressed += (_, _) => RaiseCancelled();
    }

    private void RaiseCancelled() => Cancelled?.Invoke(this, EventArgs.Empty);

    private void StartMutePolling()
    {
        _muteTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _muteTimer.Tick -= OnMuteTick;
        _muteTimer.Tick += OnMuteTick;
        _muteTimer.Start();
    }

    private void StopMutePolling() => _muteTimer?.Stop();

    private void OnMuteTick(object? sender, EventArgs e) => _window?.SetMicMuted(SafeIsMuted());

    private bool SafeIsMuted()
    {
        try { return _capture.IsInputMuted(); }
        catch { return false; }
    }

    private static IBrush Brush(string key) =>
        Avalonia.Application.Current is { } app && app.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.White;

    private static string DescribeTarget(TargetControl t)
    {
        if (t.Kind == ControlKind.Sensitive) return "Sensitive field — dictation blocked";
        var app = string.IsNullOrWhiteSpace(t.WindowTitle) ? t.ProcessName : t.WindowTitle;
        return string.IsNullOrWhiteSpace(t.ControlName) ? app : $"{app}  ›  {t.ControlName}";
    }
}
