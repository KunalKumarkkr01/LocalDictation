using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>The searchable history window; DataContext is a <see cref="HistoryViewModel"/> from DI.</summary>
public partial class HistoryWindow : Window
{
    /// <summary>Creates the window and binds the view model.</summary>
    public HistoryWindow(HistoryViewModel vm)
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
