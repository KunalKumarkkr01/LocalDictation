using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The compact acrylic listening capsule: a pulsing dot, a live white waveform, and the target
/// app. Small (~200px), bottom-centered, non-activating. Uses the Windows 11 DWM system backdrop
/// for the glass/acrylic effect and rounded corners.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 13;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];

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

    /// <summary>Updates the waveform to the current input level (0..1).</summary>
    public void SetLevel(double level)
    {
        for (int i = 0; i < BarCount; i++)
        {
            double dist = Math.Abs(i - (BarCount - 1) / 2.0) / ((BarCount - 1) / 2.0);
            double weight = 1.0 - dist * 0.55;
            double h = 3 + level * 13 * weight;
            _bars[i].Height = Math.Max(3, h);
            _bars[i].Opacity = 0.55 + (1 - dist) * 0.45;
        }
    }

    /// <summary>Sets the accent for the current stage (monochrome: white in all states).</summary>
    public void SetStage(string stage, Brush accent)
    {
        Dot.Fill = accent;
        foreach (var b in _bars) b.Fill = accent;
    }

    /// <summary>Sets the "target app" descriptor on the right of the capsule.</summary>
    public void SetTarget(string text) => TargetText.Text = text;
}
