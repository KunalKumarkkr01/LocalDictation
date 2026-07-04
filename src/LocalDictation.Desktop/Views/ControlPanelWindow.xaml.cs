using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>The control panel window; DataContext is a <see cref="ControlPanelViewModel"/> from DI.</summary>
public partial class ControlPanelWindow : Window
{
    /// <summary>Creates the window and binds the view model.</summary>
    public ControlPanelWindow(ControlPanelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();
}
