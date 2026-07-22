using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Avalonia.Views;

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
/// A command-palette overlay listing enabled personas. Port of the WPF <c>PersonaPickerWindow</c>.
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
        this.FindControl<ListBox>("List")!.ItemsSource = _items;
        Deactivated += (_, _) => { if (IsVisible) Complete(null); };
    }

    /// <inheritdoc />
    public Task<Persona?> PickAsync()
    {
        _tcs = new TaskCompletionSource<Persona?>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.FindControl<TextBlock>("HeaderLabel")!.Text = "PERSONA · " + _personas.PickerHotkey.ToUpperInvariant();
        var search = this.FindControl<TextBox>("Search")!;
        search.Text = "";
        Rebuild("");
        Show(); Activate(); search.Focus();
        return _tcs.Task;
    }

    private void Rebuild(string? filter)
    {
        _items.Clear();
        foreach (var p in _personas.Personas)
        {
            if (!p.Enabled) continue;
            if (!string.IsNullOrWhiteSpace(filter) && !p.Name.Contains(filter!, StringComparison.OrdinalIgnoreCase)) continue;
            _items.Add(new PickerItem { Persona = p });
        }
        var list = this.FindControl<ListBox>("List")!;
        if (_items.Count > 0) list.SelectedIndex = 0;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
        => Rebuild(this.FindControl<TextBox>("Search")!.Text);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var list = this.FindControl<ListBox>("List")!;
        switch (e.Key)
        {
            case Key.Escape: Complete(null); e.Handled = true; break;
            case Key.Enter: Accept(); e.Handled = true; break;
            case Key.Down: list.SelectedIndex = Math.Min(list.SelectedIndex + 1, _items.Count - 1); e.Handled = true; break;
            case Key.Up: list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.Handled = true; break;
        }
    }

    private void OnAccept(object? sender, TappedEventArgs e) => Accept();
    private void Accept() => Complete((this.FindControl<ListBox>("List")!.SelectedItem as PickerItem)?.Persona);

    private void Complete(Persona? chosen)
    {
        Hide();
        _tcs?.TrySetResult(chosen);
        _tcs = null;
    }
}
