using System.Windows.Input;
using System.Windows.Interop;
using LocalDictation.Application.Abstractions;
using LocalDictation.Infrastructure.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows;

/// <summary>
/// Global activation hotkey via <c>RegisterHotKey</c> on a hidden message-only window.
/// </summary>
/// <remarks>
/// Chosen over a low-level keyboard hook for privacy and reliability (design §7.1): the OS
/// delivers <c>WM_HOTKEY</c> with near-zero overhead and no keystroke interception, so no
/// anti-virus/EDR keylogger heuristics are triggered. Push-to-talk (hook-based) is a future opt-in.
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0x0B00; // arbitrary unique id
    private static readonly nint HwndMessage = new(-3);

    private readonly ILogger<HotkeyService> _log;
    private HwndSource? _source;
    private bool _registered;

    /// <inheritdoc />
    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>Creates the service and its message-only window.</summary>
    public HotkeyService(ILogger<HotkeyService> log)
    {
        _log = log;
        var p = new HwndSourceParameters("LocalDictation.Hotkey") { ParentWindow = HwndMessage };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <inheritdoc />
    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParse(gesture, out var mods, out var vk))
        {
            _log.LogWarning("Could not parse hotkey gesture '{Gesture}'.", gesture);
            return false;
        }

        _registered = NativeMethods.RegisterHotKey(_source!.Handle, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        if (_registered) _log.LogInformation("Registered global hotkey '{Gesture}'.", gesture);
        else _log.LogWarning("Hotkey '{Gesture}' is already in use by another app.", gesture);
        return _registered;
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs());
        }
        return nint.Zero;
    }

    /// <summary>Parses a gesture like "Ctrl+Win+Space" into modifier flags and a virtual key.</summary>
    private static bool TryParse(string gesture, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; break;
                case "alt": mods |= NativeMethods.MOD_ALT; break;
                case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                case "win": case "windows": case "super": mods |= NativeMethods.MOD_WIN; break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var key) && key != Key.None)
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}
