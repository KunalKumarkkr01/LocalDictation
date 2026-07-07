using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LocalDictation.Services;

/// <summary>
/// Renders the LocalDictation app mark (the waveform capsule, ADR-0014) to a bitmap so it can be used
/// as the menu-bar tray icon and every window's icon — the Avalonia analogue of the packaged
/// <c>tray.ico</c> resource used by the Windows app, produced in-process so no binary asset is needed.
/// </summary>
public static class AppIconFactory
{
    // The app mark drawn in a 64x64 space (2x the theme's 32-space path), waveform bars as holes.
    private const string MarkPath =
        "F0 M18,20 A14,14 0 0 1 46,20 L46,44 A14,14 0 0 1 18,44 Z " +
        "M21,24 h5.2 v16 h-5.2 Z M29.4,16 h5.2 v32 h-5.2 Z M37.8,22 h5.2 v20 h-5.2 Z";

    /// <summary>Builds a fresh 64x64 <see cref="WindowIcon"/> of the app mark on a transparent ground.</summary>
    /// <returns>A window/tray icon usable for both the taskbar/menu-bar and window chrome.</returns>
    public static WindowIcon CreateWindowIcon()
    {
        var bmp = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            var geometry = Geometry.Parse(MarkPath);
            ctx.DrawGeometry(new SolidColorBrush(Color.Parse("#F4F2EF")), null, geometry);
        }
        return new WindowIcon(bmp);
    }
}
