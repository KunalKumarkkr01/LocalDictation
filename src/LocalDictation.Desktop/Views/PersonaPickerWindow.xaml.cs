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
        ? "picker only" : "auto · " + string.Join(", ", Persona.MatchProcessNames);
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
    }

    /// <inheritdoc />
    public Task<Persona?> PickAsync()
    {
        _tcs = new TaskCompletionSource<Persona?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Search.Text = "";
        Rebuild("");
        PositionBottomCenter();
        Show(); Activate();
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
        if (_items.Count > 0) List.SelectedIndex = 0;
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
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height - 320;
    }
}
