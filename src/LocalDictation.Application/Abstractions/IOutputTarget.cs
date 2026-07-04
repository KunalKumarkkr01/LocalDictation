using LocalDictation.Domain;

namespace LocalDictation.Application.Abstractions;

/// <summary>Outcome of an attempt to deliver text to a destination.</summary>
/// <param name="Success">Whether delivery succeeded.</param>
/// <param name="StrategyUsed">Which strategy actually delivered (for diagnostics).</param>
/// <param name="Detail">Optional human-readable detail on failure.</param>
public readonly record struct OutputResult(bool Success, string StrategyUsed, string Detail = "")
{
    /// <summary>Convenience for a successful delivery.</summary>
    public static OutputResult Ok(string strategy) => new(true, strategy);
    /// <summary>Convenience for a failed delivery.</summary>
    public static OutputResult Failed(string strategy, string detail) => new(false, strategy, detail);
}

/// <summary>
/// A destination for final text: focused-control insertion, clipboard, floating editor, etc.
/// </summary>
/// <remarks>
/// Targets are ordered by <see cref="Priority"/> (higher first). The router tries each
/// applicable target until one succeeds, falling back to the always-available editor.
/// </remarks>
public interface IOutputTarget
{
    /// <summary>Stable key used to order strategies from settings (e.g. "clipboard").</summary>
    string Key { get; }

    /// <summary>Selection priority — higher targets are attempted first (fallback ordering).</summary>
    int Priority { get; }

    /// <summary>Whether this target can handle the given control.</summary>
    bool CanHandle(TargetControl target);

    /// <summary>Delivers <paramref name="text"/> to <paramref name="target"/>.</summary>
    Task<OutputResult> DeliverAsync(string text, TargetControl target, CancellationToken ct = default);
}

/// <summary>Selects and drives the appropriate <see cref="IOutputTarget"/> for a result.</summary>
public interface IOutputRouter
{
    /// <summary>
    /// Routes <paramref name="text"/> to the best available target, falling back down the
    /// chain (and finally to the floating editor) until delivery succeeds.
    /// </summary>
    Task<OutputResult> RouteAsync(string text, TargetControl target, CancellationToken ct = default);
}
