using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Extracts a focused app's real icon (Chrome, PowerShell, Windows Terminal, …) from its executable
/// for display on the listening capsule. Uses the shell's associated icon so it matches what the user
/// sees in the taskbar. Results are cached by path; callers fall back to the app mark when null.
/// </summary>
/// <remarks>
/// Uses <c>SHGetFileInfo</c> + WPF's <see cref="Imaging.CreateBitmapSourceFromHIcon"/> so no
/// System.Drawing dependency is needed in this WPF-only project.
/// </remarks>
public sealed class AppIconProvider
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000; // 32x32, crisp when shown at 16px

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The icon for the app at <paramref name="exePath"/>, or null when the path is missing or the
    /// icon can't be read (caller falls back to the app mark). Cached per path.
    /// </summary>
    /// <param name="exePath">Full path to the app's executable.</param>
    /// <returns>A frozen <see cref="ImageSource"/>, or null.</returns>
    /// <example>ForExecutable(@"C:\...\chrome.exe") → the Chrome logo.</example>
    public ImageSource? ForExecutable(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        return _cache.GetOrAdd(exePath!, Extract);
    }

    private static ImageSource? Extract(string exePath)
    {
        var info = new SHFILEINFO();
        var res = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (res == nint.Zero || info.hIcon == nint.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }
    }
}
