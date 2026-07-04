using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows;

/// <summary>
/// Captures the foreground window (Win32) and focused control (UI Automation), classifying
/// it so the pipeline can decide between insertion and the floating editor.
/// </summary>
/// <remarks>
/// Win32 gives the fast window facts; UIA gives control type, editability and — critically —
/// password detection (<c>IsPassword</c>) so sensitive fields are hard-blocked (design §7.2–7.3).
/// UIA can be slow or throw on some controls, so every UIA access is defensive and falls back
/// to class-name heuristics.
/// </remarks>
public sealed class Win32Inspector : IWindowInspector
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
    private static readonly HashSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase)
        { "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "alacritty" };

    private readonly ILogger<Win32Inspector> _log;

    /// <summary>Creates the inspector.</summary>
    public Win32Inspector(ILogger<Win32Inspector> log) => _log = log;

    /// <inheritdoc />
    public TargetControl CaptureFocusedTarget()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == nint.Zero) return TargetControl.Unknown;

        var title = GetWindowText(hwnd);
        var (process, elevated) = GetProcessInfo(hwnd);

        // Defaults from Win32 heuristics; refined by UIA below.
        var kind = ControlKind.Unknown;
        var controlName = string.Empty;
        var editable = false;
        var sensitive = false;

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                controlName = SafeName(focused);
                sensitive = SafeBool(focused, AutomationElement.IsPasswordProperty);
                if (sensitive)
                {
                    kind = ControlKind.Sensitive;
                }
                else
                {
                    kind = Classify(focused, process, out editable);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "UIA focused-element inspection failed; using Win32 fallback.");
            kind = Terminals.Contains(process) ? ControlKind.Terminal : ControlKind.Unknown;
            editable = kind == ControlKind.Terminal;
        }

        return new TargetControl
        {
            WindowHandle = hwnd,
            ProcessName = process,
            WindowTitle = title,
            ControlName = controlName,
            Kind = kind,
            IsEditable = editable && !sensitive,
            IsSensitive = sensitive,
            IsElevated = elevated
        };
    }

    /// <summary>Classifies a non-password focused element and reports editability.</summary>
    private ControlKind Classify(AutomationElement el, string process, out bool editable)
    {
        editable = false;
        var ct = SafeControlType(el);
        var hasValue = el.TryGetCurrentPattern(ValuePattern.Pattern, out _);
        var hasText = el.TryGetCurrentPattern(TextPattern.Pattern, out _);

        if (Terminals.Contains(process)) { editable = true; return ControlKind.Terminal; }

        bool isEdit = ct == ControlType.Edit || ct == ControlType.Document || hasValue || hasText;
        if (!isEdit) return ControlKind.Unsupported;

        editable = true;
        if (Browsers.Contains(process)) return ControlKind.BrowserTextArea;
        if (ct == ControlType.Document || hasText) return ControlKind.RichTextEditor;
        return ControlKind.EditableTextBox;
    }

    private static string GetWindowText(nint hwnd)
    {
        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private (string process, bool elevated) GetProcessInfo(nint hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            bool elevated = false;
            try { _ = p.MainModule; } // access-denied here usually means an elevated process
            catch (System.ComponentModel.Win32Exception) { elevated = true; }
            return (p.ProcessName, elevated);
        }
        catch { return ("unknown", false); }
    }

    private static string SafeName(AutomationElement el)
    {
        try { return el.Current.Name ?? string.Empty; } catch { return string.Empty; }
    }

    private static bool SafeBool(AutomationElement el, AutomationProperty prop)
    {
        try { return (bool)el.GetCurrentPropertyValue(prop); } catch { return false; }
    }

    private static ControlType? SafeControlType(AutomationElement el)
    {
        try { return el.Current.ControlType; } catch { return null; }
    }
}
