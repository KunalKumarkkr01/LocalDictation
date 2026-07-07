using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Windows.Output;

/// <summary>
/// Routes final text to the best available insertion strategy, honouring the user's
/// configured order and privacy toggles, and falling back to the floating editor when no
/// strategy can deliver (design §7.4 / §9.1).
/// </summary>
public sealed class OutputRouter : IOutputRouter
{
    private readonly IReadOnlyList<IOutputTarget> _targets;
    private readonly IFloatingEditor _editor;
    private readonly IUiDispatcher _ui;
    private readonly AppSettings _settings;
    private readonly ILogger<OutputRouter> _log;

    /// <summary>Creates the router from the registered insertion targets.</summary>
    public OutputRouter(
        IEnumerable<IOutputTarget> targets,
        IFloatingEditor editor,
        IUiDispatcher ui,
        AppSettings settings,
        ILogger<OutputRouter> log)
    {
        _targets = targets.ToList();
        _editor = editor;
        _ui = ui;
        _settings = settings;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<OutputResult> RouteAsync(string text, TargetControl target, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return OutputResult.Failed("none", "No text to deliver.");

        // Only hard-block sensitive fields. UIA editability detection is unreliable for Chromium
        // omniboxes and terminals, so we still attempt insertion for non-sensitive targets and let
        // the floating editor be the fallback if every strategy genuinely fails.
        if (target.IsSensitive)
        {
            _log.LogInformation("Target is sensitive; opening floating editor.");
            ShowEditor(text, target, EditorReason.Sensitive);
            return OutputResult.Failed("editor", "Sensitive field.");
        }

        // Elevated windows reject synthesized input from a standard-user process (UIPI).
        if (target.IsElevated)
        {
            _log.LogInformation("Target window is elevated; opening floating editor (UIPI).");
            ShowEditor(text, target, EditorReason.Elevated);
            return OutputResult.Failed("editor", "Elevated target (UIPI).");
        }

        // If the user moved off the field we captured (clicked away, alt-tabbed), inserting now would
        // type into the wrong app. Open the editor instead so the text is never lost or misdirected.
        if (_settings.EditorOnFocusLoss && HasForegroundMovedAway(target))
        {
            _log.LogInformation("Foreground changed since capture; opening floating editor instead of inserting.");
            ShowEditor(text, target, EditorReason.FocusMoved);
            return OutputResult.Failed("editor", "Focus moved.");
        }

        foreach (var t in OrderedTargets())
        {
            if (!t.CanHandle(target)) continue;
            var result = await t.DeliverAsync(text, target, ct);
            if (result.Success)
            {
                _log.LogInformation("Delivered via {Strategy}.", result.StrategyUsed);
                return result;
            }
            _log.LogDebug("Strategy {Strategy} failed: {Detail}", t.Key, result.Detail);
        }

        _log.LogInformation("All insertion strategies failed; opening floating editor.");
        ShowEditor(text, target, EditorReason.InsertFailed);
        return OutputResult.Failed("editor", "All strategies failed.");
    }

    /// <summary>
    /// True when the foreground window at delivery time differs from the one captured when dictation
    /// started — i.e. the user moved away from the original field. A zero captured handle (unknown
    /// target) is treated as "not moved" so we still attempt insertion.
    /// </summary>
    private static bool HasForegroundMovedAway(TargetControl target)
    {
        if (target.WindowHandle == nint.Zero) return false;
        var current = NativeMethods.GetForegroundWindow();
        return current != nint.Zero && current != target.WindowHandle;
    }

    /// <summary>Orders targets by the user's configured strategy order, then by priority.</summary>
    private IEnumerable<IOutputTarget> OrderedTargets()
    {
        var order = _settings.InsertionOrder ?? new List<string>();
        int Rank(IOutputTarget t)
        {
            if (_settings.NeverUseClipboard && t.Key == "clipboard") return int.MaxValue;
            var idx = order.IndexOf(t.Key);
            return idx >= 0 ? idx : 1000 - t.Priority;
        }
        return _targets
            .Where(t => !(_settings.NeverUseClipboard && t.Key == "clipboard"))
            .OrderBy(Rank);
    }

    private void ShowEditor(string text, TargetControl target, EditorReason reason) =>
        _ui.Post(() => _editor.ShowFor(text, target, reason));
}
