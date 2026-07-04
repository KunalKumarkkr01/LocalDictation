using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LocalDictation.Desktop.ViewModels;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// First-run onboarding wizard window. Walks the user through mic check, speech-model download, the
/// optional AI toggle, and the hotkey, then closes with <c>DialogResult = true</c> once finished.
/// Navigation and the mic-capture lifecycle live on <see cref="OnboardingViewModel"/>.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly OnboardingViewModel _vm;

    /// <summary>Creates the wizard bound to its view model.</summary>
    /// <param name="vm">Onboarding state and actions (injected).</param>
    public OnboardingWindow(OnboardingViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnNext(object sender, RoutedEventArgs e) => _vm.Next();

    private void OnBack(object sender, RoutedEventArgs e) => _vm.Back();

    private async void OnDownload(object sender, RoutedEventArgs e) => await _vm.DownloadSelectedAsync();

    private async void OnDone(object sender, RoutedEventArgs e)
    {
        await _vm.FinishAsync();
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Ensures the mic-check capture is released if the window closes at any point.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.StopMic();
        base.OnClosing(e);
    }
}
