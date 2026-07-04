using System.Windows;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Desktop.Services;
using LocalDictation.Desktop.Views;
using LocalDictation.Infrastructure;
using LocalDictation.Infrastructure.DependencyInjection;
using LocalDictation.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalDictation.Desktop;

/// <summary>
/// Application composition root. Loads settings, wires every Infrastructure adapter to its
/// port, brings up the tray, registers the global hotkey and warms the speech model — then
/// lives quietly in the background until the hotkey fires.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _provider;
    private TrayHost? _tray;
    private DictationController? _controller;

    /// <summary>Boots the DI container and background services on launch.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // ---- Load settings before building the container (many singletons capture them). ----
        var paths = new AppPaths();
        var settingsStore = new JsonSettingsStore(paths.SettingsFile, NullLogger<JsonSettingsStore>.Instance);
        var settings = settingsStore.LoadAsync().GetAwaiter().GetResult();

        // ---- UI singletons that must be created on this (UI) thread. ----
        var editor = new FloatingEditorWindow();
        _tray = new TrayHost();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(paths);
        services.AddSingleton(settings);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddInfrastructure();

        // Desktop UI ports
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IOverlayController, OverlayController>();
        services.AddSingleton<IFloatingEditor>(editor);
        services.AddSingleton<INotificationService>(_tray);
        services.AddSingleton<DictationController>();

        // On-demand windows + view models
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.HistoryViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<HistoryWindow>();

        _provider = services.BuildServiceProvider();

        // ---- Bring the app to life. ----
        _provider.GetRequiredService<IHistoryRepository>().InitializeAsync().GetAwaiter().GetResult();

        _controller = _provider.GetRequiredService<DictationController>();
        _controller.Initialize();

        _tray.DictateRequested += (_, _) => _controller.TriggerManually();
        _tray.SettingsRequested += (_, _) => ShowSingletonWindow<SettingsWindow>();
        _tray.HistoryRequested += (_, _) => ShowSingletonWindow<HistoryWindow>();
        _tray.QuitRequested += (_, _) => Shutdown();

        StartupRegistration.Apply(settings.StartWithWindows);

        _tray.Info("LocalDictation is ready", $"Press {settings.Hotkey} anywhere to dictate.");
    }

    /// <summary>Shows (or focuses) a single instance of an on-demand window.</summary>
    private void ShowSingletonWindow<T>() where T : Window
    {
        var existing = Windows.OfType<T>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        var window = _provider!.GetRequiredService<T>();
        window.Show();
        window.Activate();
    }

    /// <summary>Cleans up services and the tray on exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _tray?.Dispose();
        _provider?.Dispose();
        base.OnExit(e);
    }
}
