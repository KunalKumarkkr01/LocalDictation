using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Infrastructure;
using LocalDictation.Infrastructure.DependencyInjection;
using LocalDictation.Infrastructure.Mac.DependencyInjection;
using LocalDictation.Infrastructure.Persistence;
using LocalDictation.Services;
using LocalDictation.ViewModels;
using LocalDictation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalDictation;

/// <summary>
/// Application composition root for the macOS Avalonia shell. Loads settings, wires every
/// Infrastructure adapter to its port, brings up the menu-bar tray, registers the global hotkey and
/// warms the speech model — then lives quietly in the background until the hotkey fires. Mirrors the
/// Windows <c>App.xaml.cs</c> boot sequence, minus Velopack/auto-update.
/// </summary>
public partial class App : Avalonia.Application
{
    private ServiceProvider? _provider;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private TrayIcon? _tray;
    private MacDictationController? _controller;
    private AppSettings? _settings;

    private ControlPanelWindow? _controlPanel;
    private HistoryWindow? _history;

    /// <summary>Loads the XAML (FluentTheme + monochrome styles).</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Runs the DI boot + activation sequence once the framework is ready.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _lifetime = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            StartupLog.Reset();
            try { Boot(); StartupLog.Write("startup complete."); }
            catch (Exception ex) { StartupLog.Write("STARTUP FAILED: " + ex); }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Runs the composition + activation sequence (wrapped for diagnostics).</summary>
    private void Boot()
    {
        StartupLog.Write("Boot: loading settings…");

        // ---- Load settings before building the container (many singletons capture them). ----
        var paths = new AppPaths();
        var settingsStore = new JsonSettingsStore(paths.SettingsFile, NullLogger<JsonSettingsStore>.Instance);
        // Run async init on a thread-pool thread so awaited I/O continuations don't try to resume on
        // this (blocked) UI thread — otherwise startup deadlocks.
        var settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
        _settings = settings;

        // ---- UI singletons that must be created on this (UI) thread. ----
        var editor = new FloatingEditorWindow();
        StartupLog.Write("Boot: editor created.");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(paths);
        services.AddSingleton(settings);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddCoreInfrastructure();
        services.AddMacInfrastructure();

        // Desktop UI ports
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IOverlayController, AvaloniaOverlayController>();
        services.AddSingleton<IFloatingEditor>(editor);
        services.AddSingleton<INotificationService, MenuBarNotificationService>();
        services.AddSingleton<MacDictationController>();

        // On-demand view models
        services.AddTransient<ControlPanelViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<OnboardingViewModel>();

        _provider = services.BuildServiceProvider();

        // ---- Bring the app to life. ----
        var history = _provider.GetRequiredService<IHistoryRepository>();
        Task.Run(() => history.InitializeAsync()).GetAwaiter().GetResult();

        // Prune history older than the retention window (favorites/pinned exempt). 0 or less = keep forever.
        if (settings.HistoryRetentionDays > 0)
            _ = Task.Run(() => history.PruneAsync(TimeSpan.FromDays(settings.HistoryRetentionDays)));

        StartupLog.Write("Boot: provider built, history initialized.");

        SetUpTray(settings);

        // First run: if no speech model is installed, guide the user through mic check + model download
        // before wiring the controller. Shown non-modally (this headless app has no owner window); the
        // rest of activation resumes when the wizard closes.
        var models = _provider.GetRequiredService<ISpeechModelManager>();
        if (!models.List().Any(m => m.Installed))
        {
            StartupLog.Write("Boot: first run — showing onboarding.");
            var onboarding = new OnboardingWindow(_provider.GetRequiredService<OnboardingViewModel>());
            onboarding.Closed += (_, _) => { StartupLog.Write("Boot: onboarding closed."); Activate(settings); };
            onboarding.Show();
        }
        else
        {
            Activate(settings);
        }
    }

    /// <summary>Wires the controller, autostart and background warm-up once onboarding (if any) is done.</summary>
    private void Activate(AppSettings settings)
    {
        _controller = _provider!.GetRequiredService<MacDictationController>();
        _controller.Initialize();
        StartupLog.Write("Boot: controller initialized (hotkey registered).");

        LaunchAgentRegistration.Apply(settings.StartWithWindows);

        // If AI enhancement was left on, warm the Ollama stack at boot so it's ready without opening
        // the control panel.
        if (settings.AiEnabled)
            _ = Task.Run(async () =>
            {
                try
                {
                    StartupLog.Write("Boot AI: starting Ollama…");
                    var ok = await _provider!.GetRequiredService<IOllamaLifecycle>().EnableAsync(settings.LlmModel);
                    StartupLog.Write($"Boot AI: EnableAsync returned {ok}.");
                }
                catch (Exception ex) { StartupLog.Write("Boot AI failed: " + ex); }
            });

        _provider!.GetRequiredService<INotificationService>()
            .Info("LocalDictation is ready", $"Press {settings.Hotkey} anywhere to dictate.");
    }

    /// <summary>Builds the menu-bar tray icon and its native menu (Dictate / History / Panel / Quit).</summary>
    private void SetUpTray(AppSettings settings)
    {
        var menu = new NativeMenu();
        menu.Add(MenuItem("Dictate now", (_, _) => _controller?.TriggerManually()));
        menu.Add(MenuItem("History", (_, _) => ShowHistory()));
        menu.Add(MenuItem("Control panel", (_, _) => ShowControlPanel()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(MenuItem("Quit", (_, _) => _lifetime?.Shutdown()));

        _tray = new TrayIcon
        {
            Icon = AppIconFactory.CreateWindowIcon(),
            ToolTipText = "LocalDictation — press your hotkey to speak",
            IsVisible = true,
            Menu = menu
        };
        _tray.Clicked += (_, _) => _controller?.TriggerManually();

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private static NativeMenuItem MenuItem(string header, EventHandler onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += onClick;
        return item;
    }

    /// <summary>Shows (or focuses) the single control-panel window instance.</summary>
    private void ShowControlPanel()
    {
        if (_controlPanel is null)
        {
            _controlPanel = new ControlPanelWindow(_provider!.GetRequiredService<ControlPanelViewModel>());
            _controlPanel.OpenHistoryRequested += (_, _) => ShowHistory();
        }
        _controlPanel.Show();
        _controlPanel.Activate();
    }

    /// <summary>Shows (or focuses) the single history window instance.</summary>
    private void ShowHistory()
    {
        _history ??= new HistoryWindow(_provider!.GetRequiredService<HistoryViewModel>());
        _history.Show();
        _history.Activate();
    }
}
