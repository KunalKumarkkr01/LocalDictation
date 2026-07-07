using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Input;

/// <summary>
/// Registers the system-wide activation hotkey on macOS via the Carbon <c>RegisterEventHotKey</c> API
/// — the counterpart of the Windows <c>RegisterHotKey</c> adapter. Parses the same gesture string
/// (e.g. "Ctrl+Shift+Space") and raises <see cref="HotkeyPressed"/> on the captured UI context.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CarbonHotkeyService : IHotkeyService
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // Carbon modifier masks.
    private const uint CmdKey = 0x100, ShiftKey = 0x200, OptionKey = 0x800, ControlKey = 0x1000;
    private const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint EventHotKeyPressed = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec { public uint eventClass; public uint eventKind; }
    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID { public uint signature; public uint id; }

    private delegate int EventHandlerProc(IntPtr callRef, IntPtr evt, IntPtr userData);

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(
        IntPtr target, EventHandlerProc handler, int numTypes, EventTypeSpec[] list, IntPtr userData, out IntPtr outRef);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target, uint options, out IntPtr outHotKey);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKey);

    private readonly ILogger<CarbonHotkeyService> _log;
    private readonly EventHandlerProc _handler; // kept alive for the native handler's lifetime
    private readonly System.Threading.SynchronizationContext? _ui;

    private IntPtr _handlerRef;
    private IntPtr _hotKeyRef;

    /// <inheritdoc />
    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>Creates the hotkey service, capturing the current synchronization context for callbacks.</summary>
    public CarbonHotkeyService(ILogger<CarbonHotkeyService> log)
    {
        _log = log;
        _handler = OnHotKey;
        _ui = System.Threading.SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParse(gesture, out uint keyCode, out uint modifiers))
        {
            _log.LogWarning("Could not parse hotkey gesture '{Gesture}'.", gesture);
            return false;
        }

        var target = GetApplicationEventTarget();
        var spec = new[] { new EventTypeSpec { eventClass = EventClassKeyboard, eventKind = EventHotKeyPressed } };
        if (InstallEventHandler(target, _handler, spec.Length, spec, IntPtr.Zero, out _handlerRef) != 0)
            return false;

        var id = new EventHotKeyID { signature = 0x4C444354 /* 'LDCT' */, id = 1 };
        if (RegisterEventHotKey(keyCode, modifiers, id, target, 0, out _hotKeyRef) != 0)
        {
            Unregister();
            return false;
        }
        _log.LogInformation("Registered global hotkey '{Gesture}'.", gesture);
        return true;
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (_hotKeyRef != IntPtr.Zero) { UnregisterEventHotKey(_hotKeyRef); _hotKeyRef = IntPtr.Zero; }
        if (_handlerRef != IntPtr.Zero) { RemoveEventHandler(_handlerRef); _handlerRef = IntPtr.Zero; }
    }

    private int OnHotKey(IntPtr callRef, IntPtr evt, IntPtr userData)
    {
        void Raise() => HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs());
        if (_ui != null) _ui.Post(_ => Raise(), null);
        else Raise();
        return 0; // noErr
    }

    /// <summary>Parses "Ctrl+Shift+Space" into a Carbon keycode + modifier mask.</summary>
    private static bool TryParse(string gesture, out uint keyCode, out uint modifiers)
    {
        keyCode = 0; modifiers = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        bool haveKey = false;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ControlKey; break;
                case "shift": modifiers |= ShiftKey; break;
                case "alt": case "option": case "opt": modifiers |= OptionKey; break;
                case "cmd": case "command": case "win": case "meta": case "super": modifiers |= CmdKey; break;
                default:
                    if (!TryKeyCode(raw, out keyCode)) return false;
                    haveKey = true;
                    break;
            }
        }
        return haveKey;
    }

    /// <summary>Maps a key name to its Carbon virtual keycode (the common dictation-hotkey subset).</summary>
    private static bool TryKeyCode(string key, out uint code)
    {
        code = key.ToLowerInvariant() switch
        {
            "space" => 49, "return" or "enter" => 36, "tab" => 48, "escape" or "esc" => 53,
            "a" => 0, "s" => 1, "d" => 2, "f" => 3, "h" => 4, "g" => 5, "z" => 6, "x" => 7,
            "c" => 8, "v" => 9, "b" => 11, "q" => 12, "w" => 13, "e" => 14, "r" => 15, "y" => 16,
            "t" => 17, "o" => 31, "u" => 32, "i" => 34, "p" => 35, "l" => 37, "j" => 38, "k" => 40,
            "n" => 45, "m" => 46,
            "f1" => 122, "f2" => 120, "f3" => 99, "f4" => 118, "f5" => 96, "f6" => 97,
            "f7" => 98, "f8" => 100, "f9" => 101, "f10" => 109, "f11" => 103, "f12" => 111,
            _ => uint.MaxValue,
        };
        return code != uint.MaxValue;
    }

    /// <inheritdoc />
    public void Dispose() => Unregister();
}
