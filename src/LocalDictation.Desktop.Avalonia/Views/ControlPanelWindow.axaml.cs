using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
        viewModel.ConfirmModelChange = ConfirmModelChangeAsync; // let the VM ask before switching/downloading a model

        // Hide (not close) on the close button so the singleton instance and its bindings survive.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCancelModelDownload(object? sender, RoutedEventArgs e) => Vm.CancelModelDownload();

    /// <summary>
    /// Shows a small modal confirm before switching (and, if needed, downloading) a speech model,
    /// warning about the download size when the model isn't installed. Returns true to proceed.
    /// (Avalonia has no built-in MessageBox, so this builds a lightweight themed dialog.)
    /// </summary>
    private async Task<bool> ConfirmModelChangeAsync(ModelChangePrompt p)
    {
        var caption = p.IsInstalled ? "Switch model" : "Download model";
        var message = p.IsInstalled
            ? $"Switch the speech model to {p.Model}?"
            : $"The {p.Model} model isn't installed yet.\n\nDownload it now? This is about {FormatSize(p.DownloadBytes)} and may take a while.";

        var dialog = new Window
        {
            Title = caption,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#0A0A0B"), // app ground, matches the control panel
        };

        var proceed = new Button { Content = p.IsInstalled ? "Switch" : "Download" };
        proceed.Classes.Add("primary");
        var cancel = new Button { Content = "Cancel" };
        cancel.Classes.Add("ghost");
        proceed.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var title = new TextBlock { Text = caption };
        title.Classes.Add("h2");
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        body.Classes.Add("desc");

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(26, 24, 26, 22),
            Spacing = 16,
            Children =
            {
                title,
                body,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, proceed },
                },
            },
        };

        return await dialog.ShowDialog<bool>(this);
    }

    /// <summary>Formats a byte count as a rough "N MB" / "N.N GB" string for the confirm prompt.</summary>
    private static string FormatSize(long bytes)
    {
        var mb = bytes / 1024d / 1024d;
        return mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB";
    }

    private void OnReloadModel(object? sender, RoutedEventArgs e) => _ = Vm.ReloadModelAsync();

    private void OnRunSelfTest(object? sender, RoutedEventArgs e) => _ = Vm.RunSelfTestAsync();

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = Vm.RefreshStatusAsync();

    private void OnOpenHistory(object? sender, RoutedEventArgs e) =>
        OpenHistoryRequested?.Invoke(this, EventArgs.Empty);
}
