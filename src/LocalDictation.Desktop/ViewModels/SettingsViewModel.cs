using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>
/// Backs the settings window: exposes editable copies of <see cref="AppSettings"/>, the
/// available microphones and Whisper models, and commands to save, download models and probe
/// the local LLM. All persistence flows through <see cref="ISettingsStore"/>.
/// </summary>
/// <remarks>
/// Properties are hand-written (no MVVM source generator) to avoid the WPF markup-compiler's
/// temp-project double-emitting generated members.
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly ISpeechModelManager _models;
    private readonly ITextProcessor _processor;
    private readonly IHotkeyService _hotkey;

    /// <summary>Creates the view model and loads current values.</summary>
    public SettingsViewModel(
        AppSettings settings, ISettingsStore store, IAudioCaptureService audio,
        ISpeechModelManager models, ITextProcessor processor, IHotkeyService hotkey)
    {
        _settings = settings; _store = store; _models = models; _processor = processor; _hotkey = hotkey;

        _hotkeyValue = settings.Hotkey;
        Microphones = new ObservableCollection<string>(new[] { "System default" }.Concat(audio.GetInputDevices()));
        _selectedMicrophone = settings.MicrophoneDevice ?? "System default";
        WhisperModels = new ObservableCollection<SpeechModelSize>(Enum.GetValues<SpeechModelSize>());
        _selectedModel = settings.WhisperModel ?? models.RecommendedForHardware();
        _aiEnabled = settings.AiEnabled;
        _llmModel = settings.LlmModel;
        Modes = new ObservableCollection<ProcessingMode>(Enum.GetValues<ProcessingMode>());
        _defaultMode = settings.DefaultMode;
        _translationTarget = settings.TranslationTarget;
        _neverUseClipboard = settings.NeverUseClipboard;
        _blockedApps = string.Join(", ", settings.BlockedApps);
        _historyEnabled = settings.HistoryEnabled;
        _retentionDays = settings.HistoryRetentionDays;
        _startWithWindows = settings.StartWithWindows;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync);

        RefreshModelStatus();
        _ = RefreshLlmStatusAsync();
    }

    private string _hotkeyValue;
    public string Hotkey { get => _hotkeyValue; set => SetProperty(ref _hotkeyValue, value); }

    private string _selectedMicrophone;
    public string SelectedMicrophone { get => _selectedMicrophone; set => SetProperty(ref _selectedMicrophone, value); }

    private SpeechModelSize _selectedModel;
    public SpeechModelSize SelectedModel
    {
        get => _selectedModel;
        set { if (SetProperty(ref _selectedModel, value)) RefreshModelStatus(); }
    }

    private bool _aiEnabled;
    public bool AiEnabled { get => _aiEnabled; set => SetProperty(ref _aiEnabled, value); }

    private string _llmModel;
    public string LlmModel { get => _llmModel; set => SetProperty(ref _llmModel, value); }

    private ProcessingMode _defaultMode;
    public ProcessingMode DefaultMode { get => _defaultMode; set => SetProperty(ref _defaultMode, value); }

    private string _translationTarget;
    public string TranslationTarget { get => _translationTarget; set => SetProperty(ref _translationTarget, value); }

    private bool _neverUseClipboard;
    public bool NeverUseClipboard { get => _neverUseClipboard; set => SetProperty(ref _neverUseClipboard, value); }

    private string _blockedApps;
    public string BlockedApps { get => _blockedApps; set => SetProperty(ref _blockedApps, value); }

    private bool _historyEnabled;
    public bool HistoryEnabled { get => _historyEnabled; set => SetProperty(ref _historyEnabled, value); }

    private int _retentionDays;
    public int RetentionDays { get => _retentionDays; set => SetProperty(ref _retentionDays, value); }

    private bool _startWithWindows;
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }

    private string _modelStatus = "";
    public string ModelStatus { get => _modelStatus; set => SetProperty(ref _modelStatus, value); }

    private string _llmStatus = "Checking…";
    public string LlmStatus { get => _llmStatus; set => SetProperty(ref _llmStatus, value); }

    private string _saveStatus = "";
    public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }

    private double _downloadProgress;
    public double DownloadProgress { get => _downloadProgress; set => SetProperty(ref _downloadProgress, value); }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }

    /// <summary>Microphone device names.</summary>
    public ObservableCollection<string> Microphones { get; }
    /// <summary>Whisper model sizes.</summary>
    public ObservableCollection<SpeechModelSize> WhisperModels { get; }
    /// <summary>Available processing modes.</summary>
    public ObservableCollection<ProcessingMode> Modes { get; }

    /// <summary>Persists settings; re-applies hotkey + startup.</summary>
    public IAsyncRelayCommand SaveCommand { get; }
    /// <summary>Downloads the selected Whisper model.</summary>
    public IAsyncRelayCommand DownloadModelCommand { get; }

    private async Task SaveAsync()
    {
        _settings.Hotkey = Hotkey.Trim();
        _settings.MicrophoneDevice = SelectedMicrophone == "System default" ? null : SelectedMicrophone;
        _settings.WhisperModel = SelectedModel;
        _settings.AiEnabled = AiEnabled;
        _settings.LlmModel = LlmModel.Trim();
        _settings.DefaultMode = DefaultMode;
        _settings.TranslationTarget = TranslationTarget.Trim();
        _settings.NeverUseClipboard = NeverUseClipboard;
        _settings.BlockedApps = BlockedApps
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settings.HistoryEnabled = HistoryEnabled;
        _settings.HistoryRetentionDays = RetentionDays;
        _settings.StartWithWindows = StartWithWindows;

        await _store.SaveAsync(_settings);
        _hotkey.Register(_settings.Hotkey);
        Services.StartupRegistration.Apply(StartWithWindows);
        SaveStatus = "Saved ✓";
    }

    private async Task DownloadModelAsync()
    {
        IsDownloading = true;
        DownloadProgress = 0;
        var progress = new Progress<double>(p => DownloadProgress = p);
        var result = await _models.DownloadAsync(SelectedModel, progress);
        IsDownloading = false;
        ModelStatus = result.IsSuccess ? "Installed ✓" : $"Failed: {result.Error}";
        RefreshModelStatus();
    }

    private void RefreshModelStatus() =>
        ModelStatus = _models.IsInstalled(SelectedModel) ? "Installed ✓" : "Not downloaded";

    private async Task RefreshLlmStatusAsync()
    {
        var ok = await _processor.IsAvailableAsync();
        LlmStatus = ok ? $"Connected · {_processor.Name}" : "Not available (transcription still works)";
    }
}
