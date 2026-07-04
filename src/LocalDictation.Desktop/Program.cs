using System;
using Velopack;

namespace LocalDictation.Desktop;

/// <summary>
/// Process entry point. Exists so Velopack's install/update/uninstall hooks run before anything else:
/// <c>VelopackApp.Build().Run()</c> must be the very first line, because Velopack re-launches this exe
/// with special arguments during those lifecycle events and expects to handle them and exit before the
/// WPF application starts. Selected as the entry point via <c>&lt;StartupObject&gt;</c> in the csproj.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the Velopack lifecycle bootstrap, then boots the WPF application.
    /// </summary>
    /// <param name="args">Standard process arguments (also inspected by Velopack).</param>
    [STAThread]
    private static void Main(string[] args)
    {
        // First line, exactly once. On a normal launch this is a no-op that returns immediately;
        // during install/update/uninstall it performs the hook work and exits the process.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
