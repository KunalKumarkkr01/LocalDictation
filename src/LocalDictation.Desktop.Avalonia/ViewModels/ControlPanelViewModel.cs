using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using LocalDictation.Services;

namespace LocalDictation.ViewModels;

/// <summary>
/// Backs the control panel. Status at a glance, one AI toggle that drives the Ollama lifecycle, and the
/// core settings. Changes apply immediately (no Save button) and persist to disk. Avalonia port of the
/// WPF ControlPanelViewModel.
/// </summary>
/// <remarks>
/// Hand-written properties (via ObservableObject.SetProperty). Ollama status arrives on a background
/// thread and is marshalled to the UI via <see cref="IUiDispatcher"/>.
/// </remarks>
public sealed class ControlPanelViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly ISpeechModelManager _models;
    private readonly IHotkeyService _hotkey;
    private readonly IOllamaLifecycle _ollama;
    private readonly IUiDispatcher _ui;
    private readonly ISpeechEngine _speech;
    private readonly IReadinessService _readiness;
    private readonly IDictationSelfTest _selfTest;
    private readonly PersonaSettings _personaSettings;
    private readonly IPersonaStore _personaStore;

    /// <summary>Creates the view model and seeds it from current settings.</summary>
    public ControlPanelViewModel(
        AppSettings settings, ISettingsStore store, IAudioCaptureService audio,
        ISpeechModelManager models, IHotkeyService hotkey, IOllamaLifecycle ollama, IUiDispatcher ui,
        ISpeechEngine speech, IReadinessService readiness, IDictationSelfTest selfTest,
        PersonaSettings personas, IPersonaStore personaStore)
    {
        _settings = settings; _store = store; _models = models; _hotkey = hotkey; _ollama = ollama; _ui = ui;
        _speech = speech; _readiness = readiness; _selfTest = selfTest;

        _hotkeyValue = settings.Hotkey;
        Microphones = new ObservableCollection<string>(new[] { "System default" }.Concat(audio.GetInputDevices()));
        _selectedMicrophone = settings.MicrophoneDevice ?? "System default";
        WhisperModels = new ObservableCollection<SpeechModelSize>(Enum.GetValues<SpeechModelSize>());
        _selectedModel = settings.WhisperModel ?? models.RecommendedForHardware();
        Modes = new ObservableCollection<ProcessingMode>(Enum.GetValues<ProcessingMode>().Where(m => m != ProcessingMode.None));
        _defaultMode = settings.DefaultMode == ProcessingMode.None ? ProcessingMode.GrammarCorrection : settings.DefaultMode;
        _aiEnabled = settings.AiEnabled;
        _startAtLogin = settings.StartWithWindows;
        _notifyOnComplete = settings.NotifyOnComplete;
        _editorOnFocusLoss = settings.EditorOnFocusLoss;
        _keepHistoryForever = settings.HistoryRetentionDays <= 0;
        _retentionDays = settings.HistoryRetentionDays > 0 ? settings.HistoryRetentionDays : 30;
        _ollamaStatus = settings.AiEnabled ? "Enabled" : "Off · fast verbatim dictation";

        _ollama.StatusChanged += OnOllamaStatus;
        if (settings.AiEnabled) _ = EnableAiAsync();

        _personaSettings = personas;
        _personaStore = personaStore;
        Personas = new ObservableCollection<PersonaRowViewModel>(personas.Personas.Select(WireRow));
        _personasAutoApply = personas.AutoApply;
        _pickerHotkey = personas.PickerHotkey;
        DefaultPersonaChoices = Personas.ToList();
        _defaultPersona = Personas.FirstOrDefault(r => r.Model.Id == personas.DefaultPersonaId);

        AddPersonaCommand = new RelayCommand(AddPersona);
        EditPersonaCommand = new RelayCommand<PersonaRowViewModel>(r => { if (r != null) r.IsEditing = true; });
        CancelEditCommand = new RelayCommand<PersonaRowViewModel>(r => { if (r != null) { r.RevertFromModel(); r.IsEditing = false; } });
        SavePersonaCommand = new RelayCommand<PersonaRowViewModel>(SavePersona);
        ResetPersonaCommand = new RelayCommand<PersonaRowViewModel>(ResetPersona);
        DeletePersonaCommand = new RelayCommand<PersonaRowViewModel>(DeletePersona);

        _ = RefreshStatusAsync(); // populate the System-status section on open
    }

    // ---- System status ----
    /// <summary>Live health rows for the System-status section (speech, microphone, AI).</summary>
    public ObservableCollection<StatusItemViewModel> StatusItems { get; } = new();

    private bool _statusBusy;
    /// <summary>True while a status refresh, model reload, or self-test is running.</summary>
    public bool StatusBusy
    {
        get => _statusBusy;
        private set { if (SetProperty(ref _statusBusy, value)) OnPropertyChanged(nameof(StatusIdle)); }
    }

    /// <summary>Inverse of <see cref="StatusBusy"/> — drives button enablement.</summary>
    public bool StatusIdle => !_statusBusy;

    private string _selfTestResult = "";
    /// <summary>Latest self-test result line (empty until the test is run).</summary>
    public string SelfTestResult
    {
        get => _selfTestResult;
        private set { if (SetProperty(ref _selfTestResult, value)) OnPropertyChanged(nameof(HasSelfTestResult)); }
    }

    /// <summary>True once a self-test has produced a result line to show.</summary>
    public bool HasSelfTestResult => _selfTestResult.Length > 0;

    /// <summary>Re-checks every dependency and rebuilds the status rows.</summary>
    public async Task RefreshStatusAsync()
    {
        StatusBusy = true;
        try
        {
            var health = await _readiness.CheckAllAsync();
            _ui.Post(() =>
            {
                StatusItems.Clear();
                foreach (var h in health) StatusItems.Add(new StatusItemViewModel(h));
            });
        }
        finally { StatusBusy = false; }
    }

    /// <summary>Reloads the speech model (e.g. after downloading one) and refreshes status.</summary>
    public async Task ReloadModelAsync()
    {
        StatusBusy = true;
        try { await _speech.ReloadAsync(); }
        finally { StatusBusy = false; }
        await RefreshStatusAsync();
    }

    /// <summary>Runs the mic-free self-test and shows the result, then refreshes status.</summary>
    public async Task RunSelfTestAsync()
    {
        StatusBusy = true;
        SelfTestResult = "Running self-test…";
        try
        {
            var r = await _selfTest.RunAsync();
            SelfTestResult = r.Error is not null
                ? $"Couldn't run: {r.Error}"
                : r.Passed
                    ? $"PASS — heard “{r.Heard}” in {r.Elapsed.TotalMilliseconds:F0} ms"
                    : $"FAIL — heard “{r.Heard}” (expected “{r.Reference}”)";
        }
        finally { StatusBusy = false; }
        await RefreshStatusAsync();
    }

    // ---- Status strip ----
    /// <summary>Whisper model name for the status strip.</summary>
    public string ModelName => (_settings.WhisperModel ?? _models.RecommendedForHardware()).ToString();
    /// <summary>Active hotkey for the status strip.</summary>
    public string HotkeyDisplay => _settings.Hotkey;

    // ---- Editable ----
    private string _hotkeyValue;
    /// <summary>The activation gesture; applying it re-registers the global hotkey.</summary>
    public string Hotkey
    {
        get => _hotkeyValue;
        set { if (SetProperty(ref _hotkeyValue, value)) ApplyHotkey(value); }
    }

    private bool _aiEnabled;
    /// <summary>Whether AI enhancement is on; toggling drives the Ollama lifecycle.</summary>
    public bool AiEnabled
    {
        get => _aiEnabled;
        set
        {
            if (!SetProperty(ref _aiEnabled, value)) return;
            _settings.AiEnabled = value;
            Persist();
            if (value) _ = EnableAiAsync();
            else _ = DisableAiAsync();
        }
    }

    private ProcessingMode _defaultMode;
    /// <summary>The AI cleanup mode applied per dictation.</summary>
    public ProcessingMode DefaultMode
    {
        get => _defaultMode;
        set { if (SetProperty(ref _defaultMode, value)) { _settings.DefaultMode = value; Persist(); } }
    }

    private string _selectedMicrophone;
    /// <summary>The chosen input device ("System default" clears the override).</summary>
    public string SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { if (SetProperty(ref _selectedMicrophone, value)) { _settings.MicrophoneDevice = value == "System default" ? null : value; Persist(); } }
    }

    private SpeechModelSize _selectedModel;
    /// <summary>The active Whisper model size.</summary>
    public SpeechModelSize SelectedModel
    {
        get => _selectedModel;
        set { if (SetProperty(ref _selectedModel, value)) { _settings.WhisperModel = value; Persist(); OnPropertyChanged(nameof(ModelName)); } }
    }

    private bool _startAtLogin;
    /// <summary>Whether the app launches at login (writes/removes the LaunchAgent).</summary>
    public bool StartAtLogin
    {
        get => _startAtLogin;
        set { if (SetProperty(ref _startAtLogin, value)) { _settings.StartWithWindows = value; Persist(); LaunchAgentRegistration.Apply(value); } }
    }

    private bool _notifyOnComplete;
    /// <summary>Show a notification with the transcript after each dictation.</summary>
    public bool NotifyOnComplete
    {
        get => _notifyOnComplete;
        set { if (SetProperty(ref _notifyOnComplete, value)) { _settings.NotifyOnComplete = value; Persist(); } }
    }

    private bool _editorOnFocusLoss;
    /// <summary>Open the editor (instead of inserting) when the original field is no longer focused.</summary>
    public bool EditorOnFocusLoss
    {
        get => _editorOnFocusLoss;
        set { if (SetProperty(ref _editorOnFocusLoss, value)) { _settings.EditorOnFocusLoss = value; Persist(); } }
    }

    private bool _keepHistoryForever;
    /// <summary>When true, history is never auto-pruned (retention disabled).</summary>
    public bool KeepHistoryForever
    {
        get => _keepHistoryForever;
        set { if (SetProperty(ref _keepHistoryForever, value)) { ApplyRetention(); OnPropertyChanged(nameof(RetentionEnabled)); } }
    }

    private int _retentionDays;
    /// <summary>Days to keep non-favorite history (min 1). Ignored when <see cref="KeepHistoryForever"/>.</summary>
    public int RetentionDays
    {
        get => _retentionDays;
        set { if (SetProperty(ref _retentionDays, value < 1 ? 1 : value)) ApplyRetention(); }
    }

    /// <summary>Whether the retention-days input is active (false when keeping forever).</summary>
    public bool RetentionEnabled => !_keepHistoryForever;

    private void ApplyRetention()
    {
        _settings.HistoryRetentionDays = _keepHistoryForever ? 0 : (_retentionDays < 1 ? 1 : _retentionDays);
        Persist();
    }

    private string _ollamaStatus;
    /// <summary>Human-readable AI backend status line.</summary>
    public string OllamaStatusText { get => _ollamaStatus; private set => SetProperty(ref _ollamaStatus, value); }

    private bool _aiBusy;
    /// <summary>True while the Ollama backend is starting or loading a model.</summary>
    public bool AiBusy { get => _aiBusy; private set => SetProperty(ref _aiBusy, value); }

    /// <summary>Microphone device names.</summary>
    public ObservableCollection<string> Microphones { get; }
    /// <summary>Whisper model sizes.</summary>
    public ObservableCollection<SpeechModelSize> WhisperModels { get; }
    /// <summary>Cleanup modes (excludes None).</summary>
    public ObservableCollection<ProcessingMode> Modes { get; }

    private void ApplyHotkey(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return;
        if (_hotkey.Register(gesture))
        {
            _settings.Hotkey = gesture.Trim();
            Persist();
            OnPropertyChanged(nameof(HotkeyDisplay));
        }
    }

    private async Task EnableAiAsync()
    {
        AiBusy = true;
        await _ollama.EnableAsync(_settings.LlmModel);
        AiBusy = false;
    }

    private async Task DisableAiAsync() => await _ollama.DisableAsync(_settings.LlmModel);

    private void OnOllamaStatus(object? sender, (OllamaStatus Status, string Message) e) => _ui.Post(() =>
    {
        OllamaStatusText = e.Status == OllamaStatus.Off ? "Off · fast verbatim dictation" : e.Message;
        AiBusy = e.Status is OllamaStatus.Starting or OllamaStatus.LoadingModel;
    });

    private void Persist() => _ = _store.SaveAsync(_settings);

    // ---- Personas ----
    public ObservableCollection<PersonaRowViewModel> Personas { get; }
    public IReadOnlyList<PersonaRowViewModel> DefaultPersonaChoices { get; private set; }

    private bool _personasAutoApply;
    public bool PersonasAutoApply { get => _personasAutoApply; set { if (SetProperty(ref _personasAutoApply, value)) { _personaSettings.AutoApply = value; PersistPersonas(); } } }

    private string _pickerHotkey;
    public string PickerHotkey
    {
        get => _pickerHotkey;
        set
        {
            if (string.Equals(value, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged(nameof(PickerHotkey)); // reject: revert the bound field to the last valid value
                return;
            }
            if (SetProperty(ref _pickerHotkey, value)) { _personaSettings.PickerHotkey = value; PersistPersonas(); }
        }
    }

    private PersonaRowViewModel? _defaultPersona;
    public PersonaRowViewModel? DefaultPersona { get => _defaultPersona; set { if (SetProperty(ref _defaultPersona, value)) { _personaSettings.DefaultPersonaId = value?.Model.Id; PersistPersonas(); } } }

    public IRelayCommand AddPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> EditPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> CancelEditCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> SavePersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> ResetPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> DeletePersonaCommand { get; private set; } = null!;

    /// <summary>
    /// Constructs a persona row wired to persist immediately when its live <see cref="PersonaRowViewModel.Enabled"/>
    /// toggle changes (the Enabled switch is not part of the Save/Cancel editor form). Used everywhere rows are
    /// created — the ctor's initial list and <see cref="AddPersona"/> — so persistence stays centralized.
    /// </summary>
    private PersonaRowViewModel WireRow(Persona model)
    {
        var row = new PersonaRowViewModel(model);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonaRowViewModel.Enabled)) PersistPersonas();
        };
        return row;
    }

    private void AddPersona()
    {
        var model = new Persona { Id = "user-" + Guid.NewGuid().ToString("N")[..8], Name = "New persona", Kind = PersonaKind.User };
        _personaSettings.Personas.Add(model);
        var row = WireRow(model);
        row.IsEditing = true;
        Personas.Add(row);
        RefreshDefaultChoices();
        PersistPersonas();
    }

    private void SavePersona(PersonaRowViewModel? row)
    {
        if (row is null) return;
        row.CommitToModel();
        row.IsEditing = false;
        RefreshDefaultChoices();
        PersistPersonas();
    }

    private void ResetPersona(PersonaRowViewModel? row)
    {
        if (row is null) return;
        var seed = PersonaSeeds.DefaultPromptFor(row.Model.Id);
        if (seed != null)
        {
            row.Model.SystemPrompt = seed;
            row.SystemPrompt = seed; // sync VM state + char counter (deferred setter no longer writes Model)
            PersistPersonas();
        }
    }

    private void DeletePersona(PersonaRowViewModel? row)
    {
        if (row is null || row.Model.Kind != PersonaKind.User) return;
        _personaSettings.Personas.Remove(row.Model);
        Personas.Remove(row);
        var wasDefault = _defaultPersona == row;
        if (wasDefault) DefaultPersona = Personas.FirstOrDefault(r => r.Model.Id == "general"); // setter persists
        RefreshDefaultChoices();
        if (!wasDefault) PersistPersonas();
    }

    private void RefreshDefaultChoices()
    {
        DefaultPersonaChoices = Personas.ToList();
        OnPropertyChanged(nameof(DefaultPersonaChoices));
    }

    /// <summary>Fire-and-forget persist, mirroring <see cref="Persist"/> for settings.</summary>
    private void PersistPersonas() => _ = _personaStore.SaveAsync(_personaSettings);

    /// <summary>Serializes current personas to <paramref name="path"/> (same shape as personas.json).</summary>
    public async Task ExportAsync(string path)
    {
        // Called from an async-void code-behind handler with no error handling upstream: a failed
        // write (bad path, permissions, disk full) must not crash the app — just leave the file unwritten.
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_personaSettings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception) { }
    }

    /// <summary>Merges personas from <paramref name="path"/>: adds User personas, updates existing User
    /// prompts by id, never overwrites System/BuiltIn seeds, caps prompt length. Rebuilds the list.</summary>
    public async Task ImportAsync(string path)
    {
        // Called from an async-void code-behind handler with no error handling upstream: a user can
        // point this at any file (malformed JSON, wrong shape, unreadable). File IO and deserialization
        // both happen before any mutation of _personaSettings/Personas, so a failure here is swallowed
        // with the existing personas left completely untouched rather than crashing the app.
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var incoming = System.Text.Json.JsonSerializer.Deserialize<PersonaSettings>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (incoming?.Personas is null) return;

            foreach (var p in incoming.Personas)
            {
                if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.SystemPrompt)) continue;
                if (p.SystemPrompt.Length > 4000) p.SystemPrompt = p.SystemPrompt[..4000];
                var existing = _personaSettings.FindById(p.Id);
                if (existing is null)
                {
                    p.Kind = PersonaKind.User; // imported personas are always User
                    _personaSettings.Personas.Add(p);
                }
                else if (existing.Kind == PersonaKind.User)
                {
                    existing.Name = p.Name; existing.SystemPrompt = p.SystemPrompt;
                    existing.MatchProcessNames = p.MatchProcessNames; existing.Enabled = p.Enabled;
                }
                // System/BuiltIn seeds are never overwritten by import.
            }
            Personas.Clear();
            foreach (var m in _personaSettings.Personas) Personas.Add(WireRow(m));
            RefreshDefaultChoices();
            PersistPersonas();
        }
        catch (Exception) { }
    }
}
