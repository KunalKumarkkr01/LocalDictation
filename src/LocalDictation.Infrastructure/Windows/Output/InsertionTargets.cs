using System.Windows;
using System.Windows.Automation;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows.Output;

/// <summary>
/// Inserts text by placing it on the clipboard and synthesizing Ctrl+V, saving and
/// restoring the prior clipboard content. The most reliable general-purpose strategy
/// (rich editors, browsers, terminals), per the design's compatibility findings (§7.4).
/// </summary>
public sealed class ClipboardOutputTarget : IOutputTarget
{
    private readonly IUiDispatcher _ui;
    private readonly ILogger<ClipboardOutputTarget> _log;

    /// <summary>Creates the target.</summary>
    public ClipboardOutputTarget(IUiDispatcher ui, ILogger<ClipboardOutputTarget> log)
    {
        _ui = ui;
        _log = log;
    }

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
            // Save prior clipboard, set our text (all on STA/UI thread).
            string? prior = await _ui.InvokeAsync(() =>
            {
                string? saved = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                Clipboard.SetText(text);
                return saved;
            });

            // Re-assert the target window as foreground: transcription/AI can take a couple of
            // seconds, during which focus may have drifted (overlay, other apps). Without this the
            // paste can land in the wrong window or nowhere (design §7.4 reliability notes).
            if (target.WindowHandle != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(target.WindowHandle);
                await Task.Delay(60, ct);
            }

            SendCtrlV();

            // Wait long enough for the target to actually read the clipboard before restoring it.
            // 80 ms was too short for Chromium/terminals, so the old clipboard was restored before
            // the paste completed and nothing (or stale text) was inserted.
            await Task.Delay(350, ct);

            await _ui.InvokeAsync(() =>
            {
                if (prior is not null) Clipboard.SetText(prior);
                else Clipboard.Clear();
            });

            return OutputResult.Ok("clipboard");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Clipboard insertion failed.");
            return OutputResult.Failed("clipboard", ex.Message);
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_CONTROL),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyDown(ushort vk) => MakeKey(vk, 0);
    private static NativeMethods.INPUT KeyUp(ushort vk) => MakeKey(vk, NativeMethods.KEYEVENTF_KEYUP);
    private static NativeMethods.INPUT MakeKey(ushort vk, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };
}

/// <summary>
/// Inserts text by synthesizing Unicode keystrokes (<c>KEYEVENTF_UNICODE</c>). Respects the
/// caret and never touches the clipboard; slower for long text. Good for simple edit fields.
/// </summary>
public sealed class SendInputOutputTarget : IOutputTarget
{
    private readonly ILogger<SendInputOutputTarget> _log;

    /// <summary>Creates the target.</summary>
    public SendInputOutputTarget(ILogger<SendInputOutputTarget> log) => _log = log;

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
            var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
            foreach (char c in text)
            {
                inputs.Add(UnicodeKey(c, down: true));
                inputs.Add(UnicodeKey(c, down: false));
            }
            if (inputs.Count > 0)
                NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(),
                    System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
            return Task.FromResult(OutputResult.Ok("sendinput"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SendInput insertion failed.");
            return Task.FromResult(OutputResult.Failed("sendinput", ex.Message));
        }
    }

    private static NativeMethods.INPUT UnicodeKey(char c, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (down ? 0 : NativeMethods.KEYEVENTF_KEYUP)
            }
        }
    };
}

/// <summary>
/// Inserts text via UI Automation <see cref="ValuePattern"/> where the control supports it.
/// Sets (overwrites) the value; only viable for controls exposing the pattern.
/// </summary>
public sealed class UiaOutputTarget : IOutputTarget
{
    private readonly ILogger<UiaOutputTarget> _log;

    /// <summary>Creates the target.</summary>
    public UiaOutputTarget(ILogger<UiaOutputTarget> log) => _log = log;

    /// <inheritdoc />
    public string Key => "uia";

    /// <inheritdoc />
    public int Priority => 60;

    /// <inheritdoc />
    public bool CanHandle(TargetControl target) =>
        target.IsEditable && !target.IsSensitive && target.Kind == ControlKind.EditableTextBox;

    /// <inheritdoc />
    public Task<OutputResult> DeliverAsync(string text, TargetControl target, CancellationToken ct = default)
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is not null && el.TryGetCurrentPattern(ValuePattern.Pattern, out var pat) &&
                pat is ValuePattern vp && !vp.Current.IsReadOnly)
            {
                vp.SetValue(text);
                return Task.FromResult(OutputResult.Ok("uia"));
            }
            return Task.FromResult(OutputResult.Failed("uia", "ValuePattern unavailable."));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "UIA insertion failed.");
            return Task.FromResult(OutputResult.Failed("uia", ex.Message));
        }
    }
}
