using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalDictation.Infrastructure.Mac.Interop;

/// <summary>
/// Minimal P/Invoke surface for CoreFoundation — string bridging and reference counting, shared by
/// the macOS adapters that talk to C-level system frameworks (Accessibility, CoreGraphics).
/// </summary>
[SupportedOSPlatform("macos")]
internal static class CoreFoundation
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kCFStringEncodingUTF8
    private const uint Utf8 = 0x08000100;

    [DllImport(Lib)]
    internal static extern void CFRelease(IntPtr cf);

    [DllImport(Lib)]
    internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, byte[] cStr, uint encoding);

    [DllImport(Lib)]
    internal static extern long CFStringGetLength(IntPtr theString);

    [DllImport(Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

    /// <summary>Creates a CFString from a managed string. Caller must <see cref="CFRelease"/> it.</summary>
    internal static IntPtr ToCFString(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s + "\0");
        return CFStringCreateWithCString(IntPtr.Zero, bytes, Utf8);
    }

    /// <summary>Reads a CFString into a managed string (empty on null / failure). Does not release it.</summary>
    internal static string FromCFString(IntPtr cf)
    {
        if (cf == IntPtr.Zero) return string.Empty;
        long len = CFStringGetLength(cf);
        // Worst case 4 UTF-8 bytes per UTF-16 unit, plus terminator.
        var buffer = new byte[(len * 4) + 1];
        return CFStringGetCString(cf, buffer, buffer.Length, Utf8)
            ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0')
            : string.Empty;
    }
}
