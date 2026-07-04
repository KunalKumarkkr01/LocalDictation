using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        menu.Items.Add(MenuItem("⚙  Settings", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
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

    /// <summary>Draws a small violet rounded-square glyph as the tray icon (no .ico asset needed).</summary>
    private static ImageSource BuildIcon()
    {
        var dg = new DrawingGroup();
        var bg = new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xFF)),
            null,
            new RectangleGeometry(new Rect(0, 0, 32, 32), 8, 8));
        var dot = new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x0C, 0x0A, 0x14)),
            null,
            new EllipseGeometry(new Point(16, 13), 4.5, 4.5));
        var stem = new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x0C, 0x0A, 0x14)),
            null,
            new RectangleGeometry(new Rect(14.5, 18, 3, 6), 1.5, 1.5));
        dg.Children.Add(bg);
        dg.Children.Add(dot);
        dg.Children.Add(stem);
        var img = new DrawingImage(dg);
        img.Freeze();
        return img;
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
