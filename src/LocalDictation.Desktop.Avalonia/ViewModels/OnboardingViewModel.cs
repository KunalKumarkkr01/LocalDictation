using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.ViewModels;

/// <summary>
/// One curated speech-model choice shown on the model screen of onboarding.
/// </summary>
/// <param name="Size">The Whisper model tier this option installs.</param>
/// <param name="Name">Display name (e.g. "base.en").</param>
/// <param name="Tagline">Short character line (e.g. "Fast · English").</param>
/// <param name="SizeText">Human download size (e.g. "148 MB").</param>
/// <param name="FitText">Hardware-fit hint for this Mac.</param>
/// <param name="IsRecommended">Whether this is the pre-selected recommendation.</param>
public sealed record ModelOption(
    SpeechModelSize Size, string Name, string Tagline, string SizeText, string FitText, bool IsRecommended);

/// <summary>
/// Backs the first-run onboarding wizard: welcome → mic check → speech-model download → optional AI →
/// hotkey/ready. Reaches a working verbatim-dictation state without the optional AI stack. Avalonia
/// port of the WPF OnboardingViewModel.
/// </summary>
public sealed class OnboardingViewModel : ObservableObject
{
    /// <summary>Approximate download sizes (MB) used for the model cards and the byte progress readout.</summary>
    private static readonly Dictionary<SpeechModelSize, double> SizeMb = new()
    {
        [SpeechModelSize.Base] = 148, [SpeechModelSize.Small] = 466, [SpeechModelSize.Medium] = 1500,
    };

    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly ISpeechModelManager _models;
    private readonly IAudioCaptureService _audio;
    private readonly IUiDispatcher _ui;

    /// <summary>Creates the wizard view model and seeds the curated model list for this hardware.</summary>
    public OnboardingViewModel(
        AppSettings settings, ISettingsStore store, ISpeechModelManager models,
        IAudioCaptureService audio, IUiDispatcher ui)
    {
        _settings = settings; _store = store; _models = models; _audio = audio; _ui = ui;

        Microphones = new ObservableCollection<string>(new[] { "System default" }.Concat(audio.GetInputDevices()));
        _selectedMicrophone = settings.MicrophoneDevice ?? "System default";
        _aiEnabled = settings.AiEnabled;
        _startAtLogin = settings.StartWithWindows;

        var recommended = models.RecommendedForHardware();
        ModelOptions = new ObservableCollection<ModelOption>(BuildOptions(recommended));
        _selectedModel = ModelOptions.FirstOrDefault(o => o.IsRecommended) ?? ModelOptions[0];
    }

    private static IEnumerable<ModelOption> BuildOptions(SpeechModelSize recommended)
    {
        var pick = recommended is SpeechModelSize.Small or SpeechModelSize.Medium ? recommended : SpeechModelSize.Base;
        var tiers = new[]
        {
            (SpeechModelSize.Base,   "base.en", "Fast · English"),
            (SpeechModelSize.Small,  "small",   "Balanced · multilingual"),
            (SpeechModelSize.Medium, "medium",  "Accurate · slower"),
        };
        foreach (var (size, name, tag) in tiers)
        {
            var fit = (int)size <= (int)recommended ? "Runs well on your Mac"
                    : (int)size == (int)recommended + 1 ? "Works, a bit slower"
                    : "Heavy for this Mac";
            yield return new ModelOption(size, name, tag, $"{SizeMb[size]:0} MB".Replace("1500 MB", "1.5 GB"),
                fit, size == pick);
        }
    }

    // ---- Step navigation (0=Welcome 1=Mic 2=Model 3=AI 4=Ready) ----
    private int _step;
    /// <summary>Zero-based wizard step index.</summary>
    public int Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value)) return;
            foreach (var n in new[] { nameof(IsWelcome), nameof(IsMic), nameof(IsModel), nameof(IsAi), nameof(IsReady) })
                OnPropertyChanged(n);
        }
    }

    /// <summary>True on the welcome step.</summary>
    public bool IsWelcome => Step == 0;
    /// <summary>True on the mic-check step.</summary>
    public bool IsMic => Step == 1;
    /// <summary>True on the model-download step.</summary>
    public bool IsModel => Step == 2;
    /// <summary>True on the optional-AI step.</summary>
    public bool IsAi => Step == 3;
    /// <summary>True on the final ready step.</summary>
    public bool IsReady => Step == 4;

    /// <summary>Advances to the next step, managing the mic-capture lifecycle around the mic screen.</summary>
    public void Next()
    {
        if (Step == 1) StopMic();
        Step = Math.Min(4, Step + 1);
        if (Step == 1) StartMic();
    }

    /// <summary>Returns to the previous step.</summary>
    public void Back()
    {
        if (Step == 1) StopMic();
        Step = Math.Max(0, Step - 1);
        if (Step == 1) StartMic();
    }

    // ---- Mic check ----
    /// <summary>Available capture device names.</summary>
    public ObservableCollection<string> Microphones { get; }

    private string _selectedMicrophone;
    /// <summary>Chosen input device for the mic check.</summary>
    public string SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { if (SetProperty(ref _selectedMicrophone, value)) { _settings.MicrophoneDevice = value == "System default" ? null : value; } }
    }

    private double _micLevel;
    /// <summary>Current input level 0..1 for the meter fill.</summary>
    public double MicLevel { get => _micLevel; private set { if (SetProperty(ref _micLevel, value)) OnPropertyChanged(nameof(MicMeterWidth)); } }

    /// <summary>Meter fill width in DIPs (level scaled to the 260px track).</summary>
    public double MicMeterWidth => Math.Clamp(_micLevel, 0, 1) * 260.0;

    private bool _micOk;
    /// <summary>Latches true once a real input level is seen, so the user can't continue on a dead mic.</summary>
    public bool MicOk { get => _micOk; private set => SetProperty(ref _micOk, value); }

    /// <summary>Starts capture and routes the input level to the meter (used only during the mic screen).</summary>
    public void StartMic()
    {
        try
        {
            _audio.LevelChanged += OnLevel;
            _audio.Start();
        }
        catch { /* mic may be unavailable; the Continue button simply stays disabled */ }
    }

    /// <summary>Stops the mic-check capture and detaches the meter.</summary>
    public void StopMic()
    {
        _audio.LevelChanged -= OnLevel;
        try { _audio.Cancel(); } catch { /* nothing to cancel */ }
    }

    private void OnLevel(object? sender, double level) => _ui.Post(() =>
    {
        MicLevel = level;
        if (level > 0.06) MicOk = true;
    });

    // ---- Model download ----
    /// <summary>The three curated model choices for this run.</summary>
    public ObservableCollection<ModelOption> ModelOptions { get; }

    private ModelOption _selectedModel;
    /// <summary>The model option to download.</summary>
    public ModelOption SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

    private bool _isDownloading;
    /// <summary>True while a model download is in progress.</summary>
    public bool IsDownloading { get => _isDownloading; private set => SetProperty(ref _isDownloading, value); }

    private bool _downloadDone;
    /// <summary>True once the selected model is installed (or was already present).</summary>
    public bool DownloadDone { get => _downloadDone; private set => SetProperty(ref _downloadDone, value); }

    private double _downloadProgress;
    /// <summary>Download fraction 0..1 for the progress bar.</summary>
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }

    private string _downloadStatus = "";
    /// <summary>Human-readable download status line.</summary>
    public string DownloadStatus { get => _downloadStatus; private set => SetProperty(ref _downloadStatus, value); }

    /// <summary>
    /// Downloads the selected model (or skips if already installed), records it as the active model, and
    /// marks the step complete. Never throws; surfaces failure via <see cref="DownloadStatus"/>.
    /// </summary>
    public async Task DownloadSelectedAsync()
    {
        var size = SelectedModel.Size;
        if (_models.IsInstalled(size)) { CompleteModel(size); return; }

        IsDownloading = true; DownloadDone = false; DownloadProgress = 0;
        var totalMb = SizeMb.TryGetValue(size, out var mb) ? mb : 0;
        var progress = new Progress<double>(f =>
        {
            DownloadProgress = f;
            DownloadStatus = totalMb > 0 ? $"{f * totalMb:0} MB / {totalMb:0} MB" : $"{f * 100:0}%";
        });

        var result = await _models.DownloadAsync(size, progress);
        IsDownloading = false;
        if (result.IsSuccess) CompleteModel(size);
        else DownloadStatus = "Download failed — check your connection and retry.";
    }

    private void CompleteModel(SpeechModelSize size)
    {
        _settings.WhisperModel = size;
        _ = _store.SaveAsync(_settings);
        DownloadProgress = 1; DownloadStatus = "Installed";
        DownloadDone = true;
    }

    // ---- Optional AI ----
    private bool _aiEnabled;
    /// <summary>Whether to enable AI enhancement on finish.</summary>
    public bool AiEnabled
    {
        get => _aiEnabled;
        set { if (SetProperty(ref _aiEnabled, value)) _settings.AiEnabled = value; }
    }

    // ---- Ready ----
    /// <summary>Active hotkey to show on the final screen.</summary>
    public string HotkeyDisplay => _settings.Hotkey;

    private bool _startAtLogin;
    /// <summary>Whether the app should launch at login.</summary>
    public bool StartAtLogin
    {
        get => _startAtLogin;
        set { if (SetProperty(ref _startAtLogin, value)) _settings.StartWithWindows = value; }
    }

    /// <summary>Persists all onboarding choices. Called when the wizard finishes.</summary>
    public Task FinishAsync()
    {
        StopMic();
        return _store.SaveAsync(_settings);
    }
}
