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
[SupportedOSPlatform("macos")]
public sealed class AxWindowInspector : IWindowInspector
{
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

    private static string ProcPath(int pid)
    {
        var buffer = new byte[4096];
        int len = Accessibility.proc_pidpath(pid, buffer, (uint)buffer.Length);
        return len > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, len) : string.Empty;
    }
}
