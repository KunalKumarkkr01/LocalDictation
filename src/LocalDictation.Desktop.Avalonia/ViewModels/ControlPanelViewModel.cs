using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
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

    /// <summary>Creates the view model and seeds it from current settings.</summary>
    public ControlPanelViewModel(
        AppSettings settings, ISettingsStore store, IAudioCaptureService audio,
        ISpeechModelManager models, IHotkeyService hotkey, IOllamaLifecycle ollama, IUiDispatcher ui,
        ISpeechEngine speech, IReadinessService readiness, IDictationSelfTest selfTest)
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
}
