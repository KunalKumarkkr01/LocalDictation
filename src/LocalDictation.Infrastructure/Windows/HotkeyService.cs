using System.Windows.Input;
using System.Windows.Interop;
using LocalDictation.Application.Abstractions;
using LocalDictation.Infrastructure.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows;

/// <summary>
/// Global activation hotkey via <c>RegisterHotKey</c>, with <c>WM_HOTKEY</c> delivered through
/// <see cref="ComponentDispatcher.ThreadPreprocessMessage"/>.
/// </summary>
/// <remarks>
/// Chosen over a low-level keyboard hook for privacy and reliability (design §7.1). The thread
/// hotkey (<c>hWnd = IntPtr.Zero</c>) posts <c>WM_HOTKEY</c> to the UI thread's message queue;
/// <see cref="ComponentDispatcher"/> surfaces it. This avoids the message-only-window pitfall
/// where posted <c>WM_HOTKEY</c> messages are never dispatched to a WndProc.
/// Must be constructed and registered on the WPF UI thread.
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0x0B00;

    private readonly ILogger<HotkeyService> _log;
    private bool _registered;

    /// <inheritdoc />
    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>Creates the service and subscribes to the UI thread's message stream.</summary>
    public HotkeyService(ILogger<HotkeyService> log)
    {
        _log = log;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
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

        _registered = NativeMethods.RegisterHotKey(nint.Zero, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        if (_registered) _log.LogInformation("Registered global hotkey '{Gesture}'.", gesture);
        else _log.LogWarning("Hotkey '{Gesture}' could not be registered (reserved or in use).", gesture);
        return _registered;
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(nint.Zero, HotkeyId);
            _registered = false;
        }
    }

    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == NativeMethods.WM_HOTKEY && msg.wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs());
        }
    }

    /// <summary>Parses a gesture like "Ctrl+Shift+Space" into modifier flags and a virtual key.</summary>
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
        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
    }
}
