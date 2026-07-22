using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>One persona row in the settings list; wraps a <see cref="Persona"/> for editing.</summary>
/// <remarks>Hand-written properties (no source generators) per the WPF markup-compiler gotcha.</remarks>
public sealed class PersonaRowViewModel : ObservableObject
{
    /// <summary>The underlying persona (the source of truth persisted to personas.json).</summary>
    public Persona Model { get; }

    public PersonaRowViewModel(Persona model)
    {
        Model = model;
        _name = model.Name;
        _systemPrompt = model.SystemPrompt;
        _matchProcessNames = string.Join(", ", model.MatchProcessNames);
        _enabled = model.Enabled;
    }

    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Model.Name = value; } }

    private string _systemPrompt;
    public string SystemPrompt { get => _systemPrompt; set { if (SetProperty(ref _systemPrompt, value)) { Model.SystemPrompt = value; OnPropertyChanged(nameof(CharCount)); } } }

    private string _matchProcessNames;
    /// <summary>Comma-separated exe names bound to the editor; parsed back into the model on save.</summary>
    public string MatchProcessNames { get => _matchProcessNames; set => SetProperty(ref _matchProcessNames, value); }

    private bool _enabled;
    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) Model.Enabled = value; } }

    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

    /// <summary>Character count for the prompt editor's live counter (soft cap 1500).</summary>
    public int CharCount => _systemPrompt?.Length ?? 0;

    /// <summary>"Auto · notion.exe" / "Picker only" summary for the collapsed row.</summary>
    public string MatchSummary => Model.MatchProcessNames.Count == 0
        ? "Picker only" : "Auto · " + string.Join(", ", Model.MatchProcessNames);

    /// <summary>True for System/BuiltIn personas (Reset shown, Delete hidden).</summary>
    public bool CanReset => Model.Kind != PersonaKind.User;

    /// <summary>True for User personas (Delete shown).</summary>
    public bool CanDelete => Model.Kind == PersonaKind.User;

    /// <summary>Commits editor fields (parsing the match list) back into the model before persisting.</summary>
    public void CommitToModel()
    {
        Model.Name = Name;
        Model.SystemPrompt = SystemPrompt;
        Model.Enabled = Enabled;
        Model.MatchProcessNames = MatchProcessNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Persona.NormalizeProcessName).Where(s => s.Length > 0).Distinct().ToList();
        OnPropertyChanged(nameof(MatchSummary));
    }
}
