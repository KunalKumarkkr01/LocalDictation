using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LocalDictation.ViewModels;

namespace LocalDictation.Views;

/// <summary>
/// The Win11-SettingsCard-style control panel (rendered with native macOS chrome). Exposes System
/// status, Dictation, AI enhancement, History and General sections, all immediate-apply. Avalonia port
/// of the WPF ControlPanelWindow.
/// </summary>
public partial class ControlPanelWindow : Window
{
    /// <summary>Raised when the user asks to open the searchable History window.</summary>
    public event EventHandler? OpenHistoryRequested;

    private ControlPanelViewModel Vm => (ControlPanelViewModel)DataContext!;

    /// <summary>Creates the window and binds it to its view model.</summary>
    /// <param name="viewModel">The DI-provided control-panel view model.</param>
    public ControlPanelWindow(ControlPanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Hide (not close) on the close button so the singleton instance and its bindings survive.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnReloadModel(object? sender, RoutedEventArgs e) => _ = Vm.ReloadModelAsync();

    private void OnRunSelfTest(object? sender, RoutedEventArgs e) => _ = Vm.RunSelfTestAsync();

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = Vm.RefreshStatusAsync();

    private void OnOpenHistory(object? sender, RoutedEventArgs e) =>
        OpenHistoryRequested?.Invoke(this, EventArgs.Empty);

    private async void OnImportPersonas(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Import personas", AllowMultiple = false });
        if (files.Count > 0) await Vm.ImportAsync(files[0].Path.LocalPath);
    }

    private async void OnExportPersonas(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export personas", SuggestedFileName = "personas.json" });
        if (file != null) await Vm.ExportAsync(file.Path.LocalPath);
    }
}
