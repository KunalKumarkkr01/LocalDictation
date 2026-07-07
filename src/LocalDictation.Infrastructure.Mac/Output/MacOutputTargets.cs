using System.Diagnostics;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Mac.Input;
using LocalDictation.Infrastructure.Mac.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Output;

/// <summary>
/// Clipboard-paste insertion on macOS: puts the text on the pasteboard (<c>pbcopy</c>), synthesizes
/// ⌘V, then restores the previous clipboard. Mirrors the Windows <c>ClipboardOutputTarget</c>
/// (key "clipboard", priority 100 — the most reliable strategy).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class PasteboardOutputTarget : IOutputTarget
{
    private readonly ILogger<PasteboardOutputTarget> _log;
    /// <summary>Creates the pasteboard target.</summary>
    public PasteboardOutputTarget(ILogger<PasteboardOutputTarget> log) => _log = log;

    /// <inheritdoc />
    public string Key => "clipboard";
    /// <inheritdoc />
    public int Priority => 100;
    /// <inheritdoc />
    public bool CanHandle(TargetControl target) => !target.IsSensitive;

    /// <inheritdoc />
    public async Task<OutputResult> DeliverAsync(string text, TargetControl target, CancellationToken ct = default)
    {
        try
        {
            string previous = await PbPasteAsync(ct);
            await PbCopyAsync(text, ct);
            CoreGraphics.PostPaste();
            await Task.Delay(120, ct);         // let the target consume the paste before we restore
            await PbCopyAsync(previous, ct);
            return OutputResult.Ok(Key);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Pasteboard delivery failed.");
            return OutputResult.Failed(Key, ex.Message);
        }
    }

    private static async Task PbCopyAsync(string text, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("/usr/bin/pbcopy") { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        await p.StandardInput.WriteAsync(text.AsMemory(), ct);
        p.StandardInput.Close();
        await p.WaitForExitAsync(ct);
    }

    private static async Task<string> PbPasteAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("/usr/bin/pbpaste") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        string s = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return s;
    }
}

/// <summary>
/// Direct Unicode keystroke insertion via CGEvent — the macOS counterpart of the Windows
/// <c>SendInputOutputTarget</c> (key "sendinput", priority 80). Used when the user prefers not to
/// touch the clipboard.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CgKeystrokeOutputTarget : IOutputTarget
{
    private readonly ILogger<CgKeystrokeOutputTarget> _log;
    /// <summary>Creates the keystroke target.</summary>
    public CgKeystrokeOutputTarget(ILogger<CgKeystrokeOutputTarget> log) => _log = log;

    /// <inheritdoc />
    public string Key => "sendinput";
    /// <inheritdoc />
    public int Priority => 80;
    /// <inheritdoc />
    public bool CanHandle(TargetControl target) => !target.IsSensitive;

    /// <inheritdoc />
    public Task<OutputResult> DeliverAsync(string text, TargetControl target, CancellationToken ct = default)
    {
        try
        {
            CoreGraphics.PostUnicode(text);
            return Task.FromResult(OutputResult.Ok(Key));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Keystroke delivery failed.");
            return Task.FromResult(OutputResult.Failed(Key, ex.Message));
        }
    }
}

/// <summary>
/// Sets the focused control's value directly through the Accessibility API — the macOS counterpart
/// of the Windows UIA <c>ValuePattern</c> target (key "uia", priority 60). Only applies to editable,
/// non-sensitive controls.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class AxValueOutputTarget : IOutputTarget
{
    private readonly ILogger<AxValueOutputTarget> _log;
    /// <summary>Creates the accessibility-value target.</summary>
    public AxValueOutputTarget(ILogger<AxValueOutputTarget> log) => _log = log;

    /// <inheritdoc />
    public string Key => "uia";
    /// <inheritdoc />
    public int Priority => 60;
    /// <inheritdoc />
    public bool CanHandle(TargetControl target) => target.IsEditable && !target.IsSensitive;

    /// <inheritdoc />
    public Task<OutputResult> DeliverAsync(string text, TargetControl target, CancellationToken ct = default)
    {
        if (!Accessibility.AXIsProcessTrusted())
            return Task.FromResult(OutputResult.Failed(Key, "Accessibility permission not granted."));

        var system = Accessibility.AXUIElementCreateSystemWide();
        if (system == IntPtr.Zero) return Task.FromResult(OutputResult.Failed(Key, "No system element."));
        try
        {
            var focusedAttr = CoreFoundation.ToCFString(Accessibility.FocusedUIElement);
            var valueAttr = CoreFoundation.ToCFString(Accessibility.Value);
            var cfText = CoreFoundation.ToCFString(text);
            try
            {
                if (Accessibility.AXUIElementCopyAttributeValue(system, focusedAttr, out var focused) != 0 || focused == IntPtr.Zero)
                    return Task.FromResult(OutputResult.Failed(Key, "No focused element."));
                int rc = Accessibility.AXUIElementSetAttributeValue(focused, valueAttr, cfText);
                CoreFoundation.CFRelease(focused);
                return Task.FromResult(rc == 0 ? OutputResult.Ok(Key) : OutputResult.Failed(Key, $"AXSetValue rc={rc}"));
            }
            finally
            {
                CoreFoundation.CFRelease(focusedAttr);
                CoreFoundation.CFRelease(valueAttr);
                CoreFoundation.CFRelease(cfText);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "AX value delivery failed.");
            return Task.FromResult(OutputResult.Failed(Key, ex.Message));
        }
        finally { CoreFoundation.CFRelease(system); }
    }
}
