using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Mac.Input;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Output;

/// <summary>
/// Routes final text to the best available insertion strategy on macOS, honouring the user's
/// configured order and privacy toggles, and falling back to the floating editor when no strategy
/// can deliver. Mirrors the Windows <c>OutputRouter</c>; the only platform-specific bit is the
/// focus-moved check, which compares the frontmost pid captured at trigger time.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOutputRouter : IOutputRouter
{
    private readonly IReadOnlyList<IOutputTarget> _targets;
    private readonly IFloatingEditor _editor;
    private readonly IUiDispatcher _ui;
    private readonly AppSettings _settings;
    private readonly ILogger<MacOutputRouter> _log;

    /// <summary>Creates the router from the registered insertion targets.</summary>
    public MacOutputRouter(
        IEnumerable<IOutputTarget> targets,
        IFloatingEditor editor,
        IUiDispatcher ui,
        AppSettings settings,
        ILogger<MacOutputRouter> log)
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

        if (target.IsSensitive)
        {
            _log.LogInformation("Target is sensitive; opening floating editor.");
            ShowEditor(text, target, EditorReason.Sensitive);
            return OutputResult.Failed("editor", "Sensitive field.");
        }

        if (_settings.EditorOnFocusLoss && HasFrontmostMovedAway(target))
        {
            _log.LogInformation("Frontmost app changed since capture; opening floating editor instead of inserting.");
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

    /// <summary>True when the frontmost app at delivery time differs from the pid captured at trigger.</summary>
    private static bool HasFrontmostMovedAway(TargetControl target)
    {
        if (target.WindowHandle == nint.Zero) return false;
        int current = AxWindowInspector.FrontmostPid();
        return current != 0 && current != (int)target.WindowHandle;
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
