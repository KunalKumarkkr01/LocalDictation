using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LocalDictation.ViewModels;

namespace LocalDictation.Views;

/// <summary>
/// First-run onboarding wizard: welcome → mic check → speech-model download → optional AI → ready.
/// Reaches a working verbatim-dictation state without the optional AI stack. Avalonia port of the WPF
/// OnboardingWindow.
/// </summary>
public partial class OnboardingWindow : Window
{
    private Button _backBtn = null!;
    private Button _nextBtn = null!;

    private OnboardingViewModel Vm => (OnboardingViewModel)DataContext!;

    /// <summary>Creates the wizard and binds it to its view model.</summary>
    /// <param name="viewModel">The DI-provided onboarding view model.</param>
    public OnboardingWindow(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        _backBtn = this.FindControl<Button>("BackBtn")!;
        _nextBtn = this.FindControl<Button>("NextBtn")!;
        DataContext = viewModel;

        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OnboardingViewModel.Step)) SyncButtons(); };
        SyncButtons();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnBack(object? sender, RoutedEventArgs e) => Vm.Back();

    /// <summary>
    /// Advances the wizard. On the model step, kicks off (and awaits) the download before moving on; on
    /// the final step, persists choices and closes.
    /// </summary>
    private async void OnNext(object? sender, RoutedEventArgs e)
    {
        if (Vm.IsModel && !Vm.DownloadDone)
        {
            _nextBtn.IsEnabled = false;
            await Vm.DownloadSelectedAsync();
            _nextBtn.IsEnabled = true;
            if (!Vm.DownloadDone) return; // download failed — stay on the step so the user can retry
        }

        if (Vm.IsReady)
        {
            await Vm.FinishAsync();
            Close();
            return;
        }

        Vm.Next();
    }

    private void SyncButtons()
    {
        _backBtn.IsVisible = !Vm.IsWelcome;
        _nextBtn.Content = Vm.IsReady ? "Finish"
            : Vm.IsModel ? (Vm.DownloadDone ? "Continue" : "Download")
            : Vm.IsWelcome ? "Get started"
            : "Continue";
    }
}
