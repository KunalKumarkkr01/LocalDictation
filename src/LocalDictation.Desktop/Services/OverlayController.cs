using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LocalDictation.Application.Abstractions;
using LocalDictation.Desktop.Views;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Drives the recording overlay: positions it on the target's monitor, streams the mic level,
/// switches stage visuals, and registers a scoped ESC hotkey so the user can cancel (the overlay
/// itself never takes focus, so it cannot receive key input directly).
/// </summary>
public sealed class OverlayController : IOverlayController
{
    private const int WM_HOTKEY = 0x0312;
    private const int EscHotkeyId = 0x0E5C;
    private const uint VK_ESCAPE = 0x1B;
    private const uint MOD_NONE = 0x0000;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly IUiDispatcher _ui;
    private readonly IAudioCaptureService _capture;
    private readonly AppIconProvider _icons = new();
    private OverlayWindow? _window;
    private HwndSource? _hotkeySource;
    private DispatcherTimer? _muteTimer;

    /// <inheritdoc />
    public event EventHandler? Cancelled;

    /// <summary>Creates the controller.</summary>
    public OverlayController(IUiDispatcher ui, IAudioCaptureService capture)
    {
        _ui = ui;
        _capture = capture;
    }

    /// <inheritdoc />
    public void Show(TargetControl target) => _ui.Post(() =>
    {
        _window ??= new OverlayWindow();
        _window.SetMode(false, (Brush)_window.FindResource("RecordingBrush"));
        _window.SetTarget(DescribeTarget(target));
        _window.SetTargetIcon(_icons.ForExecutable(target.ExecutablePath) ?? (ImageSource)_window.FindResource("AppMark"));
        _window.SetLevel(0);
        _window.SetMicMuted(SafeIsMuted());
        _window.Show();
        PositionBottomCenter();
        RegisterEsc();
        StartMutePolling();
    });

    /// <inheritdoc />
    public void SetStage(OverlayStage stage, string? message = null) => _ui.Post(() =>
    {
        if (_window is null) return;
        switch (stage)
        {
            case OverlayStage.Recording:
                _window.SetMode(false, (Brush)_window.FindResource("RecordingBrush"));
                break;
            case OverlayStage.Transcribing:
            case OverlayStage.Processing:
                _window.SetMode(true, (Brush)_window.FindResource("ProcessingBrush"));
                break;
            default:
                _window.SetMode(false, (Brush)_window.FindResource("DangerBrush"));
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
        UnregisterEsc();
        _window?.Hide();
    });

    // Poll the Windows mute state while the capsule is up so unmuting mid-session updates the icon.
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

    private static string DescribeTarget(TargetControl t)
    {
        if (t.Kind == ControlKind.Sensitive) return "Sensitive field — dictation blocked";
        var app = string.IsNullOrWhiteSpace(t.WindowTitle) ? t.ProcessName : t.WindowTitle;
        return string.IsNullOrWhiteSpace(t.ControlName) ? app : $"{app}  ›  {t.ControlName}";
    }

    /// <summary>Centers the capsule at the bottom of the primary work area, just above the taskbar.</summary>
    private void PositionBottomCenter()
    {
        if (_window is null) return;
        _window.UpdateLayout();
        var wa = SystemParameters.WorkArea; // DIPs; excludes the taskbar
        _window.Left = wa.Left + (wa.Width - _window.ActualWidth) / 2;
        _window.Top = wa.Bottom - _window.ActualHeight - 10;
    }

    private void RegisterEsc()
    {
        if (_hotkeySource is null)
        {
            var p = new HwndSourceParameters("LocalDictation.EscCancel") { ParentWindow = new nint(-3) };
            _hotkeySource = new HwndSource(p);
            _hotkeySource.AddHook(EscHook);
        }
        RegisterHotKey(_hotkeySource.Handle, EscHotkeyId, MOD_NONE, VK_ESCAPE);
    }

    private void UnregisterEsc()
    {
        if (_hotkeySource is not null)
            UnregisterHotKey(_hotkeySource.Handle, EscHotkeyId);
    }

    private nint EscHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == EscHotkeyId)
        {
            handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
        return nint.Zero;
    }
}
