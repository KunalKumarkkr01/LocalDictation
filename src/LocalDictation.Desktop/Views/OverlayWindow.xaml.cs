using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The compact, non-activating recording overlay. Shows the target, a live mic level meter
/// and a pulsing status dot without ever stealing focus from the app being dictated into.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 14;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    /// <summary>Creates the overlay and builds the level-meter bars.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        BuildBars();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => StartPulse();
    }

    private void BuildBars()
    {
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 3.5,
                Height = 3,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)FindResource("RecordingBrush")
            };
            _bars[i] = bar;
            MeterPanel.Children.Add(bar);
        }
    }

    /// <summary>Applies the extended window styles that keep the overlay from taking focus.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void StartPulse()
    {
        var anim = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        PulseHalo.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Updates the meter to reflect the current input level (0..1).</summary>
    public void SetLevel(double level)
    {
        // Distribute the level across bars with a smooth centre-weighted falloff.
        for (int i = 0; i < BarCount; i++)
        {
            double dist = Math.Abs(i - (BarCount - 1) / 2.0) / ((BarCount - 1) / 2.0);
            double weight = 1.0 - dist * 0.55;
            double h = 3 + level * 19 * weight;
            _bars[i].Height = Math.Max(3, h);
        }
    }

    /// <summary>Switches the overlay between recording / transcribing / processing / error looks.</summary>
    public void SetStage(string stage, Brush accent)
    {
        StageText.Text = stage;
        StageText.Foreground = accent;
        PulseDot.Fill = accent;
        PulseHalo.Fill = accent;
        foreach (var b in _bars) b.Fill = accent;
    }

    /// <summary>Sets the "recording to" descriptor line.</summary>
    public void SetTarget(string text) => TargetText.Text = text;
}
