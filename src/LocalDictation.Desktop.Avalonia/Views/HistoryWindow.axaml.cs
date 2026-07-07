using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LocalDictation.ViewModels;

namespace LocalDictation.Views;

/// <summary>
/// The searchable, filterable dictation history (native macOS chrome). Lists past dictations with
/// copy / favourite / delete row actions. Avalonia port of the WPF HistoryWindow.
/// </summary>
public partial class HistoryWindow : Window
{
    /// <summary>Creates the window and binds it to its view model.</summary>
    /// <param name="viewModel">The DI-provided history view model.</param>
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Hide (not close) so the singleton instance survives repeated opens.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
