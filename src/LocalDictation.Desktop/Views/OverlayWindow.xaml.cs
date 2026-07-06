using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The compact acrylic listening capsule: a pulsing dot, a frequency-reactive waveform, and the
/// target app. While recording, each bar is driven by a live FFT band of the mic input (smoothed
/// at 60 fps); after you stop, it switches to a gold "working" state with a traveling shimmer.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 13;
    private const double MaxAmp = 15;      // px of headroom above the 3px rest height
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly float[] _targets = new float[BarCount]; // latest spectrum (0..1)
    private readonly float[] _cur = new float[BarCount];      // smoothed heights (px)

    private readonly DispatcherTimer _shimmer;  // indeterminate wave (processing)
    private bool _rendering;                    // subscribed to CompositionTarget.Rendering
    private double _phase;
    private bool _processing;

    private ScaleTransform _dotScale = new(1, 1);
    private double _levelTarget;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    /// <summary>Creates the capsule and its waveform bars.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        BuildBars();
        Dot.RenderTransformOrigin = new Point(0.5, 0.5);
        Dot.RenderTransform = _dotScale;

        _shimmer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _shimmer.Tick += OnShimmer;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => StartPulse();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { StopRendering(); _shimmer.Stop(); } };
    }

    // Frame-synced (~60 fps) render loop for the listening waveform — immune to the
    // Background-priority starvation that throttled a DispatcherTimer to ~19 fps.
    private void StartRendering() { if (!_rendering) { CompositionTarget.Rendering += OnRender; _rendering = true; } }
    private void StopRendering() { if (_rendering) { CompositionTarget.Rendering -= OnRender; _rendering = false; } }

    private void BuildBars()
    {
        var brush = (Brush)FindResource("RecordingBrush");
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 2.5, Height = 3, RadiusX = 2, RadiusY = 2,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = brush
            };
            _bars[i] = bar;
            MeterPanel.Children.Add(bar);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void StartPulse()
    {
        var anim = new DoubleAnimation(1.0, 0.4, TimeSpan.FromMilliseconds(950))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Dot.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Feeds the overall input level (0..1); gently scales the dot with your voice.</summary>
    public void SetLevel(double level) => _levelTarget = Math.Clamp(level, 0, 1);

    /// <summary>Feeds the latest per-band frequency magnitudes (0..1) to the waveform.</summary>
    public void SetSpectrum(float[] bands)
    {
        if (_processing || bands is null) return;
        int n = Math.Min(BarCount, bands.Length);
        for (int i = 0; i < n; i++) _targets[i] = bands[i];
    }

    /// <summary>
    /// Switches the capsule between listening (frequency-reactive, white) and processing (gold,
    /// traveling shimmer) so the state change after you stop speaking is obvious.
    /// </summary>
    /// <param name="processing">True for the transcribing/enhancing state.</param>
    /// <param name="accent">The colour for the dot and waveform.</param>
    public void SetMode(bool processing, Brush accent)
    {
        _processing = processing;
        Dot.Fill = accent;
        foreach (var b in _bars) b.Fill = accent;

        if (processing)
        {
            StopRendering();
            _phase = 0;
            _shimmer.Start();
        }
        else
        {
            _shimmer.Stop();
            Array.Clear(_targets);
            Array.Clear(_cur);
            StartRendering();
        }
    }

    // Listening: ease each bar toward its FFT band (fast attack, slower release) for a lively feel.
    private void OnRender(object? sender, EventArgs e)
    {
        for (int i = 0; i < BarCount; i++)
        {
            double target = 3 + _targets[i] * MaxAmp;
            double k = target > _cur[i] ? 0.5 : 0.22;
            _cur[i] += (float)((target - _cur[i]) * k);
            _bars[i].Height = _cur[i];
            _bars[i].Opacity = 0.45 + Math.Min(1, (_cur[i] - 3) / MaxAmp) * 0.55;
        }
        double s = _dotScale.ScaleX + (1 + _levelTarget * 0.5 - _dotScale.ScaleX) * 0.3;
        _dotScale.ScaleX = _dotScale.ScaleY = s;
    }

    // Processing: an indeterminate traveling wave.
    private void OnShimmer(object? sender, EventArgs e)
    {
        _phase += 0.45;
        for (int i = 0; i < BarCount; i++)
        {
            double s = 0.5 + 0.5 * Math.Sin(_phase - i * 0.5);
            _bars[i].Height = 3 + s * 12;
            _bars[i].Opacity = 0.35 + s * 0.65;
        }
    }

    /// <summary>Sets the "target app" descriptor on the right of the capsule.</summary>
    public void SetTarget(string text) => TargetText.Text = text;

    /// <summary>Sets the focused app's icon (already resolved, or the app-mark fallback).</summary>
    public void SetTargetIcon(ImageSource icon) => AppIcon.Source = icon;

    // A simple stroked mic glyph; the muted variant adds a diagonal slash.
    private const string MicGeometry = "M12,4 A3,3 0 0 1 15,7 L15,11 A3,3 0 0 1 9,11 L9,7 A3,3 0 0 1 12,4 Z M7,11 A5,5 0 0 0 17,11 M12,16 L12,20 M9,20 L15,20";
    private const string MicMutedGeometry = MicGeometry + " M4,4 L20,20";

    /// <summary>Reflects the live mic state: plain mic when open, red mic-with-slash when muted.</summary>
    /// <param name="muted">True when the input device is muted at the Windows level.</param>
    public void SetMicMuted(bool muted)
    {
        MicIcon.Data = Geometry.Parse(muted ? MicMutedGeometry : MicGeometry);
        MicIcon.Stroke = (Brush)FindResource(muted ? "DangerBrush" : "TextSecondaryBrush");
    }
}
