using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Owns the system-tray presence and its context menu, and doubles as the
/// <see cref="INotificationService"/> using native Windows toasts.
/// </summary>
/// <remarks>
/// The tray icon is the app's home while it lives in the background; menu actions are surfaced
/// as events so <c>App</c> can wire them without the tray knowing about windows or the pipeline.
/// </remarks>
public sealed class TrayHost : INotificationService, IDisposable
{
    private readonly TaskbarIcon _tray;

    /// <summary>Raised when the user chooses "Dictate now".</summary>
    public event EventHandler? DictateRequested;
    /// <summary>Raised when the user opens Settings.</summary>
    public event EventHandler? SettingsRequested;
    /// <summary>Raised when the user opens History.</summary>
    public event EventHandler? HistoryRequested;
    /// <summary>Raised when the user quits.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Creates and shows the tray icon with its menu.</summary>
    public TrayHost()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "LocalDictation — press your hotkey to speak",
            IconSource = BuildIcon(),
            ContextMenu = BuildMenu()
        };
        _tray.TrayLeftMouseUp += (_, _) => DictateRequested?.Invoke(this, EventArgs.Empty);
        _tray.ForceCreate();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("🎙  Dictate now", (_, _) => DictateRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(MenuItem("🕘  History", (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(MenuItem("⚙  Control panel", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Quit", (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty)));
        return menu;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    /// <summary>
    /// Loads the tray glyph from a packaged resource via a pack URI. H.NotifyIcon's IconSource
    /// pipeline resolves the icon through <c>Application.GetResourceStream</c>, so a URI-backed
    /// resource works reliably where stream-based BitmapImages / RenderTargetBitmaps throw.
    /// </summary>
    private static ImageSource BuildIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute);
        var bmp = new BitmapImage(uri);
        bmp.Freeze();
        return bmp;
    }

    /// <inheritdoc />
    public void Info(string title, string message) =>
        _tray.ShowNotification(title, message, NotificationIcon.Info);

    /// <inheritdoc />
    public void Error(string title, string message) =>
        _tray.ShowNotification(title, message, NotificationIcon.Error);

    /// <inheritdoc />
    public void Dispose() => _tray.Dispose();
}
