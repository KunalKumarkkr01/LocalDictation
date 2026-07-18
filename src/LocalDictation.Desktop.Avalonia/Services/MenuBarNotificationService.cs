using System.Diagnostics;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Services;

/// <summary>
/// Surfaces status/error notifications for the menu-bar app. On macOS there is no cross-platform toast
/// API in Avalonia, so this shells out to <c>osascript</c> to post a native Notification Center banner —
/// the functional equivalent of the Windows tray toasts (TrayHost). Never throws.
/// </summary>
/// <remarks>
/// NOTE: This is a clean functional stand-in for a native NSUserNotification. It is best-effort: if
/// osascript is unavailable the call is a no-op (the app still works, just without banners).
/// </remarks>
public sealed class MenuBarNotificationService : INotificationService
{
    /// <inheritdoc />
    public void Info(string title, string message) => Post(title, message);

    /// <inheritdoc />
    public void Error(string title, string message) => Post(title, message);

    /// <summary>Posts a Notification Center banner via osascript (macOS), swallowing any failure.</summary>
    private static void Post(string title, string message)
    {
        try
        {
            var t = Escape(title);
            var m = Escape(message);
            var script = $"display notification \"{m}\" with title \"LocalDictation\" subtitle \"{t}\"";

            // ArgumentList passes "-e" and the script as two exact, separate argv entries (no shell/
            // string re-parsing). The old code passed a single pre-joined Arguments string instead —
            // since the script itself contains double quotes (AppleScript's own string delimiters),
            // .NET's naive re-splitting of that string collided with them and cut the argument apart
            // in the wrong place, leaking bare words from the dictated text out as if they were
            // AppleScript source (surfacing as "The variable <word> is not defined" errors).
            var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            Process.Start(psi);
        }
        catch { /* notifications are best-effort */ }
    }

    /// <summary>Neutralises the quote/backslash characters that would break the AppleScript string.</summary>
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ");
}
