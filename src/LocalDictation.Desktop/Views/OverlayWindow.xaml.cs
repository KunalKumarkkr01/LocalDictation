using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The compact acrylic listening capsule: a pulsing dot, a white waveform, and the target app.
/// While recording, the waveform reflects the live mic level; after you stop, it switches to a
/// gold "working" state with a traveling-wave shimmer so you can see it transcribing/inserting.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 13;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly DispatcherTimer _shimmer;
    private double _phase;
    private bool _processing;

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
        _shimmer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _shimmer.Tick += OnShimmer;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => StartPulse();
    }

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
        var anim = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(950))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Dot.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Updates the waveform to the current input level (0..1). Ignored while processing.</summary>
    public void SetLevel(double level)
    {
        if (_processing) return;
        for (int i = 0; i < BarCount; i++)
        {
            double dist = Math.Abs(i - (BarCount - 1) / 2.0) / ((BarCount - 1) / 2.0);
            double weight = 1.0 - dist * 0.55;
            double h = 3 + level * 13 * weight;
            _bars[i].Height = Math.Max(3, h);
            _bars[i].Opacity = 0.55 + (1 - dist) * 0.45;
        }
    }

    /// <summary>
    /// Switches the capsule between listening (audio-reactive, white) and processing (gold,
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
            _phase = 0;
            _shimmer.Start();
        }
        else
        {
            _shimmer.Stop();
            foreach (var b in _bars) { b.Height = 3; b.Opacity = 0.9; }
        }
    }

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
}
