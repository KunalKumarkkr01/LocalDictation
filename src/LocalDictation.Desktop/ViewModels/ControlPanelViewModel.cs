using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>
/// A request to confirm switching the speech model, handed to the View's confirm dialog.
/// </summary>
/// <param name="Model">The model the user picked.</param>
/// <param name="IsInstalled">Whether it is already downloaded (no download needed).</param>
/// <param name="DownloadBytes">Approximate download size in bytes (0 when installed/unknown).</param>
public readonly record struct ModelChangePrompt(SpeechModelSize Model, bool IsInstalled, long DownloadBytes);

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
        _startWithWindows = settings.StartWithWindows;
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

    /// <summary>Inverse of <see cref="StatusBusy"/> — drives button enablement (no inverse converter needed).</summary>
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

    private string _selectedMicrophone;
    public string SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { if (SetProperty(ref _selectedMicrophone, value)) { _settings.MicrophoneDevice = value == "System default" ? null : value; Persist(); } }
    }

    private SpeechModelSize _selectedModel;
    private bool _suppressModelChange;
    /// <summary>
    /// The chosen Whisper model. Changing it confirms with the user, downloads the model if it isn't
    /// installed (with progress + cancellation), then reloads the engine. Cancelling reverts the choice.
    /// </summary>
    public SpeechModelSize SelectedModel
    {
        get => _selectedModel;
        set
        {
            // Reverting the combo back to the active model must not re-trigger the confirm/download flow.
            if (_suppressModelChange) { SetProperty(ref _selectedModel, value); return; }
            var previous = _selectedModel;
            if (SetProperty(ref _selectedModel, value)) _ = OnModelSelectedAsync(previous, value);
        }
    }

    // ---- Model change / download ----
    /// <summary>
    /// Set by the View to ask the user before switching (and, if needed, downloading) a model.
    /// Returns true to proceed, false to cancel. When null, changes proceed without a prompt.
    /// </summary>
    public Func<ModelChangePrompt, bool>? ConfirmModelChange { get; set; }

    private bool _isDownloadingModel;
    /// <summary>True while a model download is in flight (drives the progress row + Cancel button).</summary>
    public bool IsDownloadingModel { get => _isDownloadingModel; private set => SetProperty(ref _isDownloadingModel, value); }

    private double _modelDownloadProgress;
    /// <summary>Download fraction 0..1 for the model progress bar.</summary>
    public double ModelDownloadProgress { get => _modelDownloadProgress; private set => SetProperty(ref _modelDownloadProgress, value); }

    private string _modelDownloadStatus = "";
    /// <summary>Latest model-download status line (progress, "cancelled", or an error). Empty when idle.</summary>
    public string ModelDownloadStatus
    {
        get => _modelDownloadStatus;
        private set { if (SetProperty(ref _modelDownloadStatus, value)) OnPropertyChanged(nameof(HasModelDownloadStatus)); }
    }

    /// <summary>True when there is a model-download status line to show.</summary>
    public bool HasModelDownloadStatus => _modelDownloadStatus.Length > 0;

    private CancellationTokenSource? _modelDownloadCts;

    /// <summary>Cancels an in-progress model download; the partial file is removed and the choice reverts.</summary>
    public void CancelModelDownload() => _modelDownloadCts?.Cancel();

    /// <summary>
    /// Handles a model selection: confirms with the user, then either activates an already-installed
    /// model or downloads a missing one. On cancel/failure the selection reverts to <paramref name="previous"/>.
    /// </summary>
    private async Task OnModelSelectedAsync(SpeechModelSize previous, SpeechModelSize target)
    {
        if (target == previous) return;

        var installed = _models.IsInstalled(target);
        var proceed = ConfirmModelChange?.Invoke(
            new ModelChangePrompt(target, installed, installed ? 0 : _models.ApproximateSizeBytes(target))) ?? true;
        if (!proceed) { RevertSelectedModel(previous); return; }

        if (installed) { await ActivateModelAsync(target); return; }
        await DownloadAndActivateAsync(previous, target);
    }

    /// <summary>Downloads <paramref name="target"/> with progress + cancellation, then activates it; reverts on cancel/failure.</summary>
    private async Task DownloadAndActivateAsync(SpeechModelSize previous, SpeechModelSize target)
    {
        _modelDownloadCts?.Dispose();
        _modelDownloadCts = new CancellationTokenSource();
        var totalMb = _models.ApproximateSizeBytes(target) / 1024d / 1024d;

        StatusBusy = true;
        IsDownloadingModel = true;
        ModelDownloadProgress = 0;
        ModelDownloadStatus = $"Downloading {target}…";
        var progress = new Progress<double>(f =>
        {
            ModelDownloadProgress = f;
            ModelDownloadStatus = totalMb > 0
                ? $"Downloading {target}… {f * totalMb:0} / {totalMb:0} MB"
                : $"Downloading {target}… {f * 100:0}%";
        });

        var result = await _models.DownloadAsync(target, progress, _modelDownloadCts.Token);

        var cancelled = _modelDownloadCts.IsCancellationRequested;
        _modelDownloadCts.Dispose();
        _modelDownloadCts = null;
        IsDownloadingModel = false;

        if (result.IsSuccess)
        {
            ModelDownloadStatus = "";
            await ActivateModelAsync(target);   // persists, reloads the engine, refreshes status
        }
        else
        {
            RevertSelectedModel(previous);
            ModelDownloadStatus = cancelled ? "Download cancelled." : $"Download failed: {result.Error}";
            StatusBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>Persists <paramref name="target"/> as the active model and reloads the engine.</summary>
    private async Task ActivateModelAsync(SpeechModelSize target)
    {
        _settings.WhisperModel = target;
        Persist();
        OnPropertyChanged(nameof(ModelName));
        await ReloadModelAsync();   // toggles StatusBusy and refreshes status
    }

    /// <summary>Snaps the combo back to <paramref name="previous"/> without re-triggering the change flow.</summary>
    private void RevertSelectedModel(SpeechModelSize previous)
    {
        _suppressModelChange = true;
        try { SelectedModel = previous; }
        finally { _suppressModelChange = false; }
    }

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set { if (SetProperty(ref _startWithWindows, value)) { _settings.StartWithWindows = value; Persist(); Services.StartupRegistration.Apply(value); } }
    }

    private bool _notifyOnComplete;
    /// <summary>Show a tray toast with the transcript after each dictation.</summary>
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
