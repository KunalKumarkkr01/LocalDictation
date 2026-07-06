using System.Windows.Media;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>
/// A single System-status row for the control panel: a component's name, its one-line status, any
/// fix steps, and a colored dot brush reflecting health (off-white = ok, gold = degraded, red = down).
/// </summary>
public sealed class StatusItemViewModel
{
    /// <summary>Builds a row from a component health snapshot.</summary>
    /// <param name="health">The component's current health.</param>
    public StatusItemViewModel(ComponentHealth health)
    {
        Component = health.Component;
        Summary = health.Summary;
        Detail = health.Fixes.Count > 0 ? string.Join("  •  ", health.Fixes) : string.Empty;
        DotBrush = BrushFor(health.State);
    }

    /// <summary>Component display name (e.g. "Speech engine").</summary>
    public string Component { get; }

    /// <summary>One-line status summary.</summary>
    public string Summary { get; }

    /// <summary>Joined fix steps, shown only when the component isn't Ok.</summary>
    public string Detail { get; }

    /// <summary>True when there are fix steps to display.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Status-dot brush chosen from the health state (monochrome + sanctioned gold).</summary>
    public Brush DotBrush { get; }

    private static Brush BrushFor(HealthState state)
    {
        var key = state switch
        {
            HealthState.Ok => "ReadyBrush",
            HealthState.Degraded => "ProcessingBrush",
            _ => "DangerBrush",
        };
        return System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
