using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalDictation.Infrastructure.Mac.Interop;

/// <summary>
/// P/Invoke surface for synthesizing keyboard input on macOS via CoreGraphics <c>CGEvent</c> — the
/// counterpart of Win32 <c>SendInput</c>. Used to paste (⌘V) and to type Unicode text directly.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class CoreGraphics
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    internal const ulong FlagCommand = 0x100000; // kCGEventFlagMaskCommand
    internal const uint HidEventTap = 0;          // kCGHIDEventTap
    internal const int SourceHidState = 1;        // kCGEventSourceStateHIDSystemState
    internal const ushort KeyV = 9;               // Carbon/CG virtual keycode for 'v'

    [DllImport(Lib)]
    internal static extern IntPtr CGEventSourceCreate(int stateId);

    [DllImport(Lib)]
    internal static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(Lib)]
    internal static extern void CGEventSetFlags(IntPtr evt, ulong flags);

    [DllImport(Lib)]
    internal static extern void CGEventKeyboardSetUnicodeString(IntPtr evt, long length, char[] unicodeString);

    [DllImport(Lib)]
    internal static extern void CGEventPost(uint tap, IntPtr evt);

    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(CF)] internal static extern void CFRelease(IntPtr cf);

    /// <summary>Posts a ⌘V paste keystroke.</summary>
    internal static void PostPaste()
    {
        var src = CGEventSourceCreate(SourceHidState);
        var down = CGEventCreateKeyboardEvent(src, KeyV, true);
        CGEventSetFlags(down, FlagCommand);
        CGEventPost(HidEventTap, down);
        var up = CGEventCreateKeyboardEvent(src, KeyV, false);
        CGEventSetFlags(up, FlagCommand);
        CGEventPost(HidEventTap, up);
        CFRelease(down); CFRelease(up);
        if (src != IntPtr.Zero) CFRelease(src);
    }

    /// <summary>Types <paramref name="text"/> as a Unicode keystroke sequence.</summary>
    internal static void PostUnicode(string text)
    {
        var src = CGEventSourceCreate(SourceHidState);
        var chars = text.ToCharArray();
        var down = CGEventCreateKeyboardEvent(src, 0, true);
        CGEventKeyboardSetUnicodeString(down, chars.Length, chars);
        CGEventPost(HidEventTap, down);
        var up = CGEventCreateKeyboardEvent(src, 0, false);
        CGEventKeyboardSetUnicodeString(up, chars.Length, chars);
        CGEventPost(HidEventTap, up);
        CFRelease(down); CFRelease(up);
        if (src != IntPtr.Zero) CFRelease(src);
    }
}
