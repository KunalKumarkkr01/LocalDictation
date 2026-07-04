using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>The searchable history window; DataContext is a <see cref="HistoryViewModel"/> from DI.</summary>
public partial class HistoryWindow : Window
{
    // Segoe Fluent Icons glyphs (built from code points so the source stays ASCII-clean).
    private static readonly string MaximizeGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private Rect _restoreBounds;
    private bool _maximized;

    /// <summary>Creates the window and binds the view model.</summary>
    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; } // double-click title bar = maximize/restore
        DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Toggles between the default size and filling the working area (see ControlPanelWindow).</summary>
    private void ToggleMaximize()
    {
        if (!_maximized)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
            _maximized = true;
            MaxButton.Content = RestoreGlyph;
            MaxButton.ToolTip = "Restore";
        }
        else
        {
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            _maximized = false;
            MaxButton.Content = MaximizeGlyph;
            MaxButton.ToolTip = "Maximize";
        }
    }
}
