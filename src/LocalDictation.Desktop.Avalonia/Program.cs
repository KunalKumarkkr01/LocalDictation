using Avalonia;

namespace LocalDictation;

/// <summary>
/// Process entry point for the macOS Avalonia shell.
/// </summary>
/// <remarks>
/// Builds the Avalonia app and starts the classic desktop lifetime. The app itself runs headless
/// (menu-bar tray only, no main window) until the global hotkey fires — see <see cref="App"/>.
/// </remarks>
public static class Program
{
    /// <summary>Configures and launches the Avalonia application.</summary>
    /// <param name="args">Process command-line arguments (forwarded to the lifetime).</param>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Builds the Avalonia app configuration (platform detect + Inter font).</summary>
    /// <returns>The configured <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
