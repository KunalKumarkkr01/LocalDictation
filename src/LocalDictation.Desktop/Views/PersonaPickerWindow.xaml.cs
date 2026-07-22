using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Views;

/// <summary>UI port for choosing a persona for a single dictation.</summary>
public interface IPersonaPicker
{
    /// <summary>Shows the palette and resolves to the chosen persona, or null if cancelled.</summary>
    Task<Persona?> PickAsync();
}

/// <summary>Small view item exposing a persona's display name and match summary to the palette.</summary>
public sealed class PickerItem
{
    public required Persona Persona { get; init; }
    public string Name => Persona.Name;
    public string MatchSummary => Persona.MatchProcessNames.Count == 0
        ? "Picker only" : "Auto · " + string.Join(", ", Persona.MatchProcessNames);
    /// <summary>Monogram shown in the row badge (personas carry no icon assets).</summary>
    public string Initial => string.IsNullOrWhiteSpace(Persona.Name) ? "?" : Persona.Name.Trim()[..1].ToUpperInvariant();
    /// <summary>Provenance chip label.</summary>
    public string KindLabel => Persona.Kind switch
    {
        PersonaKind.System => "System",
        PersonaKind.BuiltIn => "Built-in",
        _ => "Custom"
    };
}

/// <summary>
/// A command-palette overlay listing enabled personas. Reuses the glass chrome + monochrome styles.
/// Non-modal (Show + focus); completes a TaskCompletionSource on select or cancel.
/// </summary>
public partial class PersonaPickerWindow : Window, IPersonaPicker
{
    private readonly PersonaSettings _personas;
    private readonly ObservableCollection<PickerItem> _items = new();
    private TaskCompletionSource<Persona?>? _tcs;

    public PersonaPickerWindow(PersonaSettings personas)
    {
        _personas = personas;
        InitializeComponent();
        List.ItemsSource = _items;
        // The window sizes to its content (variable persona count, and it shrinks/grows as you
        // filter), so re-anchor to the bottom-centre every time the measured size changes —
        // otherwise a taller list runs off the bottom of the screen under the taskbar.
        SizeChanged += (_, _) => PositionBottomCenter();
    }

    /// <inheritdoc />
    public Task<Persona?> PickAsync()
    {
        _tcs = new TaskCompletionSource<Persona?>(TaskCreationOptions.RunContinuationsAsynchronously);
        HotkeyKeys.ItemsSource = _personas.PickerHotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Search.Text = "";
        Rebuild("");
        Show(); Activate();
        PositionBottomCenter(); // ActualHeight is valid after Show()'s layout pass
        Search.Focus();
        return _tcs.Task;
    }

    private void Rebuild(string filter)
    {
        _items.Clear();
        foreach (var p in _personas.Personas)
        {
            if (!p.Enabled) continue;
            if (!string.IsNullOrWhiteSpace(filter) && !p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _items.Add(new PickerItem { Persona = p });
        }
        if (_items.Count > 0)
        {
            List.SelectedIndex = 0;
            List.ScrollIntoView(List.SelectedItem); // reset scroll to the top after filtering
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild(Search.Text);
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Complete(null); e.Handled = true; break;
            case Key.Enter: Accept(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        if (_items.Count == 0) return;
        var i = List.SelectedIndex + delta;
        List.SelectedIndex = Math.Clamp(i, 0, _items.Count - 1);
        if (List.SelectedItem is not null) List.ScrollIntoView(List.SelectedItem); // keep the highlight in view
    }

    private void OnAccept(object sender, MouseButtonEventArgs e) => Accept();
    private void Accept() => Complete((List.SelectedItem as PickerItem)?.Persona);

    private void Complete(Persona? chosen)
    {
        Hide();
        _tcs?.TrySetResult(chosen);
        _tcs = null;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible) Complete(null); // clicking away cancels
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : 0;
        Left = area.Left + (area.Width - w) / 2;
        // Anchor the bottom ~14px above the work-area bottom (i.e. above the taskbar), clamped so a
        // very tall list never spills off the top either.
        Top = Math.Max(area.Top + 8, area.Top + area.Height - h - 14);
    }
}
