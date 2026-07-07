using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace LocalDictation.Views;

/// <summary>
/// The compact listening capsule: a pulsing dot, a frequency-reactive waveform, and the target app.
/// While recording, each bar is driven by a live FFT band of the mic input (smoothed on a ~60 fps
/// timer); after you stop, it switches to a gold "working" state with a traveling shimmer. Avalonia
/// port of the WPF OverlayWindow.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 13;
    private const double MaxAmp = 15;      // px of headroom above the 3px rest height
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly float[] _targets = new float[BarCount]; // latest spectrum (0..1)
    private readonly float[] _cur = new float[BarCount];      // smoothed heights (px)

    private readonly DispatcherTimer _frame;   // unified ~60 fps render loop
    private double _phase;
    private bool _processing;
    private double _dotScale = 1.0;
    private double _levelTarget;
    private double _pulse;                       // 0..1 sine for the resting dot pulse

    private Ellipse _dot = null!;
    private StackPanel _meterPanel = null!;
    private Path _micIcon = null!;
    private Image _appIcon = null!;
    private TextBlock _targetText = null!;

    // A simple stroked mic glyph; the muted variant adds a diagonal slash.
    private const string MicGeometry = "M12,4 A3,3 0 0 1 15,7 L15,11 A3,3 0 0 1 9,11 L9,7 A3,3 0 0 1 12,4 Z M7,11 A5,5 0 0 0 17,11 M12,16 L12,20 M9,20 L15,20";
    private const string MicMutedGeometry = MicGeometry + " M4,4 L20,20";

    /// <summary>Creates the capsule and its waveform bars.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        _dot = this.FindControl<Ellipse>("Dot")!;
        _meterPanel = this.FindControl<StackPanel>("MeterPanel")!;
        _micIcon = this.FindControl<Path>("MicIcon")!;
        _appIcon = this.FindControl<Image>("AppIcon")!;
        _targetText = this.FindControl<TextBlock>("TargetText")!;

        BuildBars();
        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _frame.Tick += OnFrame;
        SizeChanged += (_, _) => PositionBottomCenter();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Centers the capsule at the bottom of the primary work area, just above the menu bar/dock.</summary>
    public void PositionBottomCenter()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var wa = screen.WorkingArea; // device pixels
        int winW = (int)(Bounds.Width * screen.Scaling);
        int winH = (int)(Bounds.Height * screen.Scaling);
        if (winW <= 0 || winH <= 0) return;
        int x = wa.X + (wa.Width - winW) / 2;
        int y = wa.Y + wa.Height - winH - (int)(20 * screen.Scaling);
        Position = new PixelPoint(x, y);
    }

    private IBrush Brush(string key) =>
        this.TryGetResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.White;

    private void BuildBars()
    {
        var brush = Brush("RecordingBrush");
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
            _meterPanel.Children.Add(bar);
        }
        SetMicMuted(false);
    }

    /// <summary>Starts the render loop when shown; stops it when hidden.</summary>
    /// <param name="e">Open event args.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _frame.Start();
        PositionBottomCenter();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _frame.Stop();
        base.OnClosed(e);
    }

    /// <summary>Feeds the overall input level (0..1); gently scales the dot with your voice.</summary>
    /// <param name="level">Normalised input level.</param>
    public void SetLevel(double level) => _levelTarget = Math.Clamp(level, 0, 1);

    /// <summary>Feeds the latest per-band frequency magnitudes (0..1) to the waveform.</summary>
    /// <param name="bands">Per-band magnitudes, one per waveform bar.</param>
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
    public void SetMode(bool processing, IBrush accent)
    {
        _processing = processing;
        _dot.Fill = accent;
        foreach (var b in _bars) b.Fill = accent;
        if (processing) _phase = 0;
        else { Array.Clear(_targets); Array.Clear(_cur); }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_processing) StepShimmer();
        else StepListening();
    }

    // Listening: ease each bar toward its FFT band (fast attack, slower release) for a lively feel;
    // the dot breathes on a slow sine and swells slightly with the input level.
    private void StepListening()
    {
        for (int i = 0; i < BarCount; i++)
        {
            double target = 3 + _targets[i] * MaxAmp;
            double k = target > _cur[i] ? 0.5 : 0.22;
            _cur[i] += (float)((target - _cur[i]) * k);
            _bars[i].Height = _cur[i];
            _bars[i].Opacity = 0.45 + Math.Min(1, (_cur[i] - 3) / MaxAmp) * 0.55;
        }
        double s = _dotScale + (1 + _levelTarget * 0.5 - _dotScale) * 0.3;
        _dotScale = s;
        _dot.RenderTransform = new ScaleTransform(s, s);

        _pulse += 0.06;
        _dot.Opacity = 0.4 + (0.5 + 0.5 * Math.Sin(_pulse)) * 0.6;
    }

    // Processing: an indeterminate traveling wave.
    private void StepShimmer()
    {
        _phase += 0.16;
        for (int i = 0; i < BarCount; i++)
        {
            double s = 0.5 + 0.5 * Math.Sin(_phase - i * 0.5);
            _bars[i].Height = 3 + s * 12;
            _bars[i].Opacity = 0.35 + s * 0.65;
        }
        _dot.Opacity = 1;
    }

    /// <summary>Sets the "target app" descriptor on the right of the capsule.</summary>
    /// <param name="text">The app/control label.</param>
    public void SetTarget(string text) => _targetText.Text = text;

    /// <summary>Sets the focused app's icon (already resolved, or the app-mark fallback).</summary>
    /// <param name="icon">The image to show.</param>
    public void SetTargetIcon(IImage? icon) => _appIcon.Source = icon;

    /// <summary>Reflects the live mic state: plain mic when open, red mic-with-slash when muted.</summary>
    /// <param name="muted">True when the input device is muted at the OS level.</param>
    public void SetMicMuted(bool muted)
    {
        _micIcon.Data = Geometry.Parse(muted ? MicMutedGeometry : MicGeometry);
        _micIcon.Stroke = Brush(muted ? "DangerBrush" : "TextSecondaryBrush");
    }
}
