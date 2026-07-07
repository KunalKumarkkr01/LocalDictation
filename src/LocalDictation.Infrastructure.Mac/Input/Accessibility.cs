using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalDictation.Infrastructure.Mac.Input;

/// <summary>
/// P/Invoke surface for the macOS Accessibility (AXUIElement) C API and <c>libproc</c>, used to inspect
/// the focused control and to set a control's value — the macOS equivalent of Win32 + UI Automation.
/// Requires the app to be granted Accessibility permission (System Settings › Privacy &amp; Security).
/// </summary>
[SupportedOSPlatform("macos")]
internal static class Accessibility
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string LibProc = "/usr/lib/libproc.dylib";

    // Common AX attribute names.
    internal const string FocusedApplication = "AXFocusedApplication";
    internal const string FocusedUIElement = "AXFocusedUIElement";
    internal const string Role = "AXRole";
    internal const string Subrole = "AXSubrole";
    internal const string Value = "AXValue";
    internal const string Title = "AXTitle";

    [DllImport(AppServices)]
    internal static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(AppServices)]
    internal static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(AppServices)]
    internal static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(AppServices)]
    internal static extern int AXUIElementGetPid(IntPtr element, out int pid);

    [DllImport(AppServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXIsProcessTrusted();

    [DllImport(LibProc)]
    internal static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);
}
