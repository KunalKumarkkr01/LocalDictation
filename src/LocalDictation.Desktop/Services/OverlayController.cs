using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly IUiDispatcher _ui;
    private OverlayWindow? _window;
    private HwndSource? _hotkeySource;

    /// <inheritdoc />
    public event EventHandler? Cancelled;

    /// <summary>Creates the controller.</summary>
    public OverlayController(IUiDispatcher ui) => _ui = ui;

    /// <inheritdoc />
    public void Show(TargetControl target) => _ui.Post(() =>
    {
        _window ??= new OverlayWindow();
        _window.SetStage("Listening", (Brush)_window.FindResource("RecordingBrush"));
        _window.SetTarget(DescribeTarget(target));
        _window.SetLevel(0);
        _window.Show();
        PositionNear(target.WindowHandle);
        RegisterEsc();
    });

    /// <inheritdoc />
    public void SetStage(OverlayStage stage, string? message = null) => _ui.Post(() =>
    {
        if (_window is null) return;
        var (label, key) = stage switch
        {
            OverlayStage.Recording => ("Listening", "RecordingBrush"),
            OverlayStage.Transcribing => ("Transcribing", "AccentBrush"),
            OverlayStage.Processing => ("Enhancing", "AccentBrush"),
            _ => ("Error", "DangerBrush")
        };
        _window.SetStage(message ?? label, (Brush)_window.FindResource(key));
    });

    /// <inheritdoc />
    public void UpdateLevel(double level) => _ui.Post(() => _window?.SetLevel(level));

    /// <inheritdoc />
    public void Hide() => _ui.Post(() =>
    {
        UnregisterEsc();
        _window?.Hide();
    });

    private static string DescribeTarget(TargetControl t)
    {
        if (t.Kind == ControlKind.Sensitive) return "Sensitive field — dictation blocked";
        var app = string.IsNullOrWhiteSpace(t.WindowTitle) ? t.ProcessName : t.WindowTitle;
        return string.IsNullOrWhiteSpace(t.ControlName) ? app : $"{app}  ›  {t.ControlName}";
    }

    /// <summary>Positions the overlay at the bottom-centre of the target window (DPI-correct).</summary>
    private void PositionNear(nint targetHwnd)
    {
        if (_window is null) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        double left, top;

        if (targetHwnd != nint.Zero && GetWindowRect(targetHwnd, out var r) && r.Right > r.Left)
        {
            double cx = (r.Left + r.Right) / 2.0 / dpi.DpiScaleX;
            double bottom = r.Bottom / dpi.DpiScaleY;
            left = cx - _window.ActualWidth / 2;
            top = bottom - _window.ActualHeight - 90;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            left = wa.Left + (wa.Width - _window.ActualWidth) / 2;
            top = wa.Bottom - _window.ActualHeight - 80;
        }
        _window.Left = left;
        _window.Top = Math.Max(20, top);
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
