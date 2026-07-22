using System.IO;
using System.Linq;
using System.Windows;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Desktop.Services;
using LocalDictation.Desktop.Views;
using LocalDictation.Infrastructure;
using LocalDictation.Infrastructure.DependencyInjection;
using LocalDictation.Infrastructure.Persistence;
using LocalDictation.Infrastructure.Windows.DependencyInjection;
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

        StartupLog.Reset();
        DispatcherUnhandledException += (_, args) =>
        { StartupLog.Write("DISPATCHER EXCEPTION: " + args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            StartupLog.Write("DOMAIN EXCEPTION: " + args.ExceptionObject);

        try { Boot(); StartupLog.Write("startup complete."); }
        catch (Exception ex)
        {
            StartupLog.Write("STARTUP FAILED: " + ex);
            System.Windows.MessageBox.Show("LocalDictation failed to start:\n\n" + ex.Message,
                "LocalDictation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Runs the composition + activation sequence (wrapped for diagnostics).</summary>
    private void Boot()
    {
        StartupLog.Write("Boot: loading settings…");

        // ---- Load settings before building the container (many singletons capture them). ----
        var paths = new AppPaths();
        var settingsStore = new JsonSettingsStore(paths.SettingsFile, NullLogger<JsonSettingsStore>.Instance);
        // Run async init on a thread-pool thread (Task.Run) so its awaited I/O continuations do
        // NOT try to resume on this blocked UI thread — otherwise startup deadlocks.
        var settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
        var personasExisted = File.Exists(paths.PersonasFile);
        var personaStore = new JsonPersonaStore(paths.PersonasFile, NullLogger<JsonPersonaStore>.Instance);
        var personas = Task.Run(() => personaStore.LoadAsync()).GetAwaiter().GetResult();
        // First-run migration: if the user already had AI on with a non-default cleanup mode, keep it as the
        // default persona instead of silently switching them to "general".
        if (!personasExisted && settings.AiEnabled)
        {
            var mappedId = LocalDictation.Application.Processing.PersonaSeeds.PersonaIdForMode(settings.DefaultMode);
            if (mappedId != null && !string.Equals(mappedId, personas.DefaultPersonaId, StringComparison.OrdinalIgnoreCase))
            {
                personas.DefaultPersonaId = mappedId;
                Task.Run(() => personaStore.SaveAsync(personas)).GetAwaiter().GetResult();
            }
        }

        // ---- UI singletons that must be created on this (UI) thread. ----
        var editor = new FloatingEditorWindow();
        _tray = new TrayHost();
        StartupLog.Write("Boot: tray + editor created.");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(paths);
        services.AddSingleton(settings);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton(personas);
        services.AddSingleton<IPersonaStore>(personaStore);
        services.AddCoreInfrastructure();
        services.AddWindowsInfrastructure();

        // Desktop UI ports
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IOverlayController, OverlayController>();
        services.AddSingleton<IFloatingEditor>(editor);
        services.AddSingleton<INotificationService>(_tray);
        services.AddSingleton<PersonaPickerWindow>();
        services.AddSingleton<IPersonaPicker>(sp => sp.GetRequiredService<PersonaPickerWindow>());
        services.AddSingleton<DictationController>();

        // On-demand windows + view models
        services.AddTransient<ViewModels.ControlPanelViewModel>();
        services.AddTransient<ViewModels.HistoryViewModel>();
        services.AddTransient<ViewModels.OnboardingViewModel>();
        services.AddTransient<ControlPanelWindow>();
        services.AddTransient<HistoryWindow>();
        services.AddTransient<OnboardingWindow>();

        _provider = services.BuildServiceProvider();

        // ---- Bring the app to life. ----
        var history = _provider.GetRequiredService<IHistoryRepository>();
        Task.Run(() => history.InitializeAsync()).GetAwaiter().GetResult();

        // Prune history older than the retention window (favorites/pinned exempt). 0 or less = keep forever.
        if (settings.HistoryRetentionDays > 0)
            _ = Task.Run(() => history.PruneAsync(TimeSpan.FromDays(settings.HistoryRetentionDays)));

        StartupLog.Write("Boot: provider built, history initialized.");

        // First run: if no speech model is installed, guide the user through mic check + model
        // download before the engine warms up. Skipped once any model is present (incl. dev repo).
        var models = _provider.GetRequiredService<ISpeechModelManager>();
        if (!models.List().Any(m => m.Installed))
        {
            StartupLog.Write("Boot: first run — showing onboarding.");
            _provider.GetRequiredService<OnboardingWindow>().ShowDialog();
            StartupLog.Write("Boot: onboarding closed.");
        }

        _controller = _provider.GetRequiredService<DictationController>();
        _controller.Initialize();
        StartupLog.Write("Boot: controller initialized (hotkey registered).");

        _tray.DictateRequested += (_, _) => _controller.TriggerManually();
        _tray.SettingsRequested += (_, _) => ShowSingletonWindow<ControlPanelWindow>();
        _tray.HistoryRequested += (_, _) => ShowSingletonWindow<HistoryWindow>();
        _tray.QuitRequested += (_, _) => Shutdown();

        StartupRegistration.Apply(settings.StartWithWindows);

        // Best-effort background auto-update (no-ops on dev builds / offline / no release yet).
        _ = Task.Run(UpdateService.CheckAsync);

        // If AI enhancement was left on, warm the Ollama stack at boot so it's ready without opening
        // the control panel (previously it only started when the panel was opened).
        if (settings.AiEnabled)
            _ = Task.Run(async () =>
            {
                try
                {
                    StartupLog.Write("Boot AI: starting Ollama…");
                    var ok = await _provider.GetRequiredService<IOllamaLifecycle>().EnableAsync(settings.LlmModel);
                    StartupLog.Write($"Boot AI: EnableAsync returned {ok}.");
                }
                catch (Exception ex) { StartupLog.Write("Boot AI failed: " + ex); }
            });

        _tray.Info("LocalDictation is ready", $"Press {settings.Hotkey} anywhere to dictate.");
    }

    /// <summary>Shows (or focuses) a single instance of an on-demand window.</summary>
    private void ShowSingletonWindow<T>() where T : Window
    {
        var existing = Windows.OfType<T>().FirstOrDefault();
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Show();   // windows are hidden (not closed) on their X button, so re-show
            existing.Activate();
            return;
        }
        var window = _provider!.GetRequiredService<T>();
        if (window is ControlPanelWindow cp)
            cp.OpenHistoryRequested += (_, _) => ShowSingletonWindow<HistoryWindow>();
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
