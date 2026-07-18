using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Mac.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Input;

/// <summary>
/// Captures a <see cref="TargetControl"/> snapshot of the focused app/control on macOS via the
/// Accessibility API — the counterpart of the Windows <c>Win32Inspector</c>. Reads the frontmost
/// application's pid (stored in <see cref="TargetControl.WindowHandle"/> for the router's
/// focus-moved check), its executable path, and the focused element's role/subrole to classify
/// editability and detect secure (password) fields.
/// </summary>
/// <remarks>
/// The systemwide <c>AXFocusedApplication</c> attribute (the primary pid source) has been observed
/// returning <c>kAXErrorNoValue</c> (-25212) in practice even with Accessibility permission granted —
/// a real AXError, not a permission failure. <see cref="TryFrontmostPidViaWindowList"/> is the fallback
/// for exactly that case: it reads the owning pid of the frontmost on-screen window via
/// <c>CGWindowListCopyWindowInfo</c> (a CoreGraphics C API — no Objective-C messaging needed), matching
/// what the design doc called the <c>NSWorkspace.frontmostApplication</c> source.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class AxWindowInspector : IWindowInspector
{
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const uint OnScreenOnly = 1 << 0;
    private const uint ExcludeDesktopElements = 1 << 4;

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    private readonly ILogger<AxWindowInspector> _log;

    /// <summary>Creates the inspector.</summary>
    public AxWindowInspector(ILogger<AxWindowInspector> log) => _log = log;

    /// <inheritdoc />
    public TargetControl CaptureFocusedTarget()
    {
        if (!Accessibility.AXIsProcessTrusted())
        {
            _log.LogWarning("Accessibility permission not granted; cannot inspect the focused control.");
            return TargetControl.Unknown;
        }

        var system = Accessibility.AXUIElementCreateSystemWide();
        if (system == IntPtr.Zero) return TargetControl.Unknown;

        try
        {
            var app = CopyElement(system, Accessibility.FocusedApplication);
            var focused = CopyElement(system, Accessibility.FocusedUIElement);

            int pid = 0;
            if (app != IntPtr.Zero) Accessibility.AXUIElementGetPid(app, out pid);
            else if (focused != IntPtr.Zero) Accessibility.AXUIElementGetPid(focused, out pid);
            if (pid <= 0) pid = TryFrontmostPidViaWindowList();

            string exePath = pid > 0 ? ProcPath(pid) : string.Empty;
            string procName = string.IsNullOrEmpty(exePath) ? "unknown"
                : Path.GetFileNameWithoutExtension(exePath);

            string role = focused != IntPtr.Zero ? CopyString(focused, Accessibility.Role) : "";
            string subrole = focused != IntPtr.Zero ? CopyString(focused, Accessibility.Subrole) : "";
            string windowTitle = app != IntPtr.Zero ? CopyString(app, Accessibility.Title) : "";
            string controlName = focused != IntPtr.Zero ? CopyString(focused, Accessibility.Title) : "";

            bool sensitive = subrole == "AXSecureTextField";
            bool editable = role is "AXTextField" or "AXTextArea" or "AXComboBox" || sensitive;

            if (app != IntPtr.Zero) CoreFoundation.CFRelease(app);
            if (focused != IntPtr.Zero) CoreFoundation.CFRelease(focused);

            return new TargetControl
            {
                WindowHandle = pid,                       // pid doubles as the "which app" handle on macOS
                ProcessName = procName,
                ExecutablePath = string.IsNullOrEmpty(exePath) ? null : exePath,
                WindowTitle = string.IsNullOrEmpty(windowTitle) ? procName : windowTitle,
                ControlName = controlName,
                Kind = sensitive ? ControlKind.Sensitive
                     : editable ? ControlKind.EditableTextBox
                     : ControlKind.Unknown,
                IsEditable = editable,
                IsSensitive = sensitive,
                IsElevated = false,                       // macOS apps aren't "elevated" like Win32 UAC
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Focused-target inspection failed.");
            return TargetControl.Unknown;
        }
        finally
        {
            CoreFoundation.CFRelease(system);
        }
    }

    /// <summary>Returns the pid of the current frontmost application (0 if unavailable).</summary>
    public static int FrontmostPid()
    {
        var system = Accessibility.AXUIElementCreateSystemWide();
        if (system == IntPtr.Zero) return 0;
        try
        {
            var app = CopyElement(system, Accessibility.FocusedApplication);
            if (app == IntPtr.Zero) return 0;
            Accessibility.AXUIElementGetPid(app, out int pid);
            CoreFoundation.CFRelease(app);
            return pid;
        }
        finally { CoreFoundation.CFRelease(system); }
    }

    private static IntPtr CopyElement(IntPtr parent, string attribute)
    {
        var attr = CoreFoundation.ToCFString(attribute);
        try
        {
            return Accessibility.AXUIElementCopyAttributeValue(parent, attr, out var val) == 0 ? val : IntPtr.Zero;
        }
        finally { CoreFoundation.CFRelease(attr); }
    }

    private static string CopyString(IntPtr element, string attribute)
    {
        var attr = CoreFoundation.ToCFString(attribute);
        try
        {
            if (Accessibility.AXUIElementCopyAttributeValue(element, attr, out var val) != 0 || val == IntPtr.Zero)
                return string.Empty;
            var s = CoreFoundation.FromCFString(val);
            CoreFoundation.CFRelease(val);
            return s;
        }
        finally { CoreFoundation.CFRelease(attr); }
    }

    /// <summary>
    /// Fallback pid source for when the AX-based focused-application lookup returns no value: reads
    /// the owning pid of the frontmost normal-layer on-screen window (layer 0) via
    /// <c>CGWindowListCopyWindowInfo</c>, which the window server orders front-to-back independent of
    /// the AX focus attributes. Returns 0 if unavailable.
    /// </summary>
    private static int TryFrontmostPidViaWindowList()
    {
        var ownerPidKey = CoreFoundation.ToCFString("kCGWindowOwnerPID");
        var layerKey = CoreFoundation.ToCFString("kCGWindowLayer");
        var list = IntPtr.Zero;
        try
        {
            list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
            if (list == IntPtr.Zero) return 0;

            long count = CoreFoundation.CFArrayGetCount(list);
            for (long i = 0; i < count; i++)
            {
                var entry = CoreFoundation.CFArrayGetValueAtIndex(list, i);
                if (entry == IntPtr.Zero) continue;

                var layer = CoreFoundation.ToInt64(CoreFoundation.CFDictionaryGetValue(entry, layerKey));
                if (layer != 0) continue; // skip menu bar / overlay / desktop-layer entries

                var pid = CoreFoundation.ToInt64(CoreFoundation.CFDictionaryGetValue(entry, ownerPidKey));
                if (pid > 0) return (int)pid;
            }
            return 0;
        }
        finally
        {
            if (list != IntPtr.Zero) CoreFoundation.CFRelease(list);
            CoreFoundation.CFRelease(ownerPidKey);
            CoreFoundation.CFRelease(layerKey);
        }
    }

    private static string ProcPath(int pid)
    {
        var buffer = new byte[4096];
        int len = Accessibility.proc_pidpath(pid, buffer, (uint)buffer.Length);
        return len > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, len) : string.Empty;
    }
}
