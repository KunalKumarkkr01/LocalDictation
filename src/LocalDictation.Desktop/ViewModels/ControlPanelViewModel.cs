using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>
/// Backs the control panel. Status at a glance, one AI toggle that drives the Ollama lifecycle,
/// and the core settings. Changes apply immediately (no Save button) and persist to disk.
/// </summary>
/// <remarks>
/// Hand-written properties (no MVVM source generator) to avoid the WPF markup-compiler conflict.
/// Ollama status arrives on a background thread and is marshalled to the UI via <see cref="IUiDispatcher"/>.
/// </remarks>
public sealed class ControlPanelViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly ISpeechModelManager _models;
    private readonly IHotkeyService _hotkey;
    private readonly IOllamaLifecycle _ollama;
    private readonly IUiDispatcher _ui;

    /// <summary>Creates the view model and seeds it from current settings.</summary>
    public ControlPanelViewModel(
        AppSettings settings, ISettingsStore store, IAudioCaptureService audio,
        ISpeechModelManager models, IHotkeyService hotkey, IOllamaLifecycle ollama, IUiDispatcher ui)
    {
        _settings = settings; _store = store; _models = models; _hotkey = hotkey; _ollama = ollama; _ui = ui;

        _hotkeyValue = settings.Hotkey;
        Microphones = new ObservableCollection<string>(new[] { "System default" }.Concat(audio.GetInputDevices()));
        _selectedMicrophone = settings.MicrophoneDevice ?? "System default";
        WhisperModels = new ObservableCollection<SpeechModelSize>(Enum.GetValues<SpeechModelSize>());
        _selectedModel = settings.WhisperModel ?? models.RecommendedForHardware();
        Modes = new ObservableCollection<ProcessingMode>(Enum.GetValues<ProcessingMode>().Where(m => m != ProcessingMode.None));
        _defaultMode = settings.DefaultMode == ProcessingMode.None ? ProcessingMode.GrammarCorrection : settings.DefaultMode;
        _aiEnabled = settings.AiEnabled;
        _livePreview = settings.LivePreview;
        _startWithWindows = settings.StartWithWindows;
        _ollamaStatus = settings.AiEnabled ? "Enabled" : "Off · fast verbatim dictation";

        _ollama.StatusChanged += OnOllamaStatus;
        if (settings.AiEnabled) _ = EnableAiAsync();
    }

    // ---- Status strip ----
    /// <summary>Whisper model name for the status strip.</summary>
    public string ModelName => (_settings.WhisperModel ?? _models.RecommendedForHardware()).ToString();
    /// <summary>Active hotkey for the status strip.</summary>
    public string HotkeyDisplay => _settings.Hotkey;

    // ---- Editable ----
    private string _hotkeyValue;
    public string Hotkey
    {
        get => _hotkeyValue;
        set { if (SetProperty(ref _hotkeyValue, value)) ApplyHotkey(value); }
    }

    private bool _aiEnabled;
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
    public ProcessingMode DefaultMode
    {
        get => _defaultMode;
        set { if (SetProperty(ref _defaultMode, value)) { _settings.DefaultMode = value; Persist(); } }
    }

    private bool _livePreview;
    public bool LivePreview
    {
        get => _livePreview;
        set { if (SetProperty(ref _livePreview, value)) { _settings.LivePreview = value; Persist(); } }
    }

    private string _selectedMicrophone;
    public string SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { if (SetProperty(ref _selectedMicrophone, value)) { _settings.MicrophoneDevice = value == "System default" ? null : value; Persist(); } }
    }

    private SpeechModelSize _selectedModel;
    public SpeechModelSize SelectedModel
    {
        get => _selectedModel;
        set { if (SetProperty(ref _selectedModel, value)) { _settings.WhisperModel = value; Persist(); OnPropertyChanged(nameof(ModelName)); } }
    }

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set { if (SetProperty(ref _startWithWindows, value)) { _settings.StartWithWindows = value; Persist(); Services.StartupRegistration.Apply(value); } }
    }

    private string _ollamaStatus;
    public string OllamaStatusText { get => _ollamaStatus; private set => SetProperty(ref _ollamaStatus, value); }

    private bool _aiBusy;
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

    private async Task DisableAiAsync()
    {
        await _ollama.DisableAsync(_settings.LlmModel);
    }

    private void OnOllamaStatus(object? sender, (OllamaStatus Status, string Message) e) => _ui.Post(() =>
    {
        OllamaStatusText = e.Status == OllamaStatus.Off ? "Off · fast verbatim dictation" : e.Message;
        AiBusy = e.Status is OllamaStatus.Starting or OllamaStatus.LoadingModel;
    });

    private void Persist() => _ = _store.SaveAsync(_settings);
}
