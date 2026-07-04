using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>The settings window; its DataContext is a <see cref="SettingsViewModel"/> from DI.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Creates the window and binds the view model.</summary>
    public SettingsWindow(SettingsViewModel vm)
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
