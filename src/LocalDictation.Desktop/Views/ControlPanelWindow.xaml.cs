using System;
using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>The control panel window; DataContext is a <see cref="ControlPanelViewModel"/> from DI.</summary>
public partial class ControlPanelWindow : Window
{
    // Segoe Fluent Icons glyphs (built from code points so the source stays ASCII-clean).
    private static readonly string MaximizeGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private Rect _restoreBounds;
    private bool _maximized;

    /// <summary>Raised when the user asks to open the history window (the "Open" button in the History card).</summary>
    public event EventHandler? OpenHistoryRequested;

    /// <summary>Creates the window and binds the view model.</summary>
    public ControlPanelWindow(ControlPanelViewModel vm)
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

    private void OnOpenHistory(object sender, RoutedEventArgs e) => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);

    private async void OnReloadModel(object sender, RoutedEventArgs e)
    {
        if (DataContext is ControlPanelViewModel vm) await vm.ReloadModelAsync();
    }

    private async void OnRunSelfTest(object sender, RoutedEventArgs e)
    {
        if (DataContext is ControlPanelViewModel vm) await vm.RunSelfTestAsync();
    }

    private async void OnRefreshStatus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ControlPanelViewModel vm) await vm.RefreshStatusAsync();
    }

    private async void OnImportPersonas(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Personas (*.json)|*.json", Title = "Import personas" };
        if (dlg.ShowDialog() == true && DataContext is ControlPanelViewModel vm)
            await vm.ImportAsync(dlg.FileName);
    }

    private async void OnExportPersonas(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Personas (*.json)|*.json", FileName = "personas.json", Title = "Export personas" };
        if (dlg.ShowDialog() == true && DataContext is ControlPanelViewModel vm)
            await vm.ExportAsync(dlg.FileName);
    }

    /// <summary>
    /// Toggles between the default size and filling the working area. Uses manual work-area bounds
    /// rather than <see cref="WindowState.Maximized"/> so a borderless, transparent window doesn't
    /// cover the taskbar or clip its rounded corners.
    /// </summary>
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
