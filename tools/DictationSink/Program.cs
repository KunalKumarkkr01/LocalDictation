using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace DictationSink;

// A controllable, focusable text target used to deterministically verify that LocalDictation
// inserts text into a real editable control. It self-activates (a fresh process may take
// foreground), focuses its textbox, and mirrors the textbox contents to a temp file every
// 300 ms so an external test can read back exactly what was inserted.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var sinkFile = Path.Combine(Path.GetTempPath(), "dictation-sink.txt");
        try { File.WriteAllText(sinkFile, ""); } catch { /* ignore */ }

        var box = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 16),
            BackColor = Color.FromArgb(20, 18, 28),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None
        };

        var form = new Form
        {
            Text = "DictationSink — dictate here",
            TopMost = true,
            Width = 700,
            Height = 240,
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(20, 18, 28)
        };
        form.Controls.Add(box);

        var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) => { try { File.WriteAllText(sinkFile, box.Text); } catch { /* ignore */ } };
        timer.Start();

        // Forcibly hold foreground for the first ~20s (test only) so a background app's paste lands
        // here reliably despite Windows' foreground lock, using the AttachThreadInput technique.
        int ticks = 0;
        var fgTimer = new System.Windows.Forms.Timer { Interval = 500 };
        fgTimer.Tick += (_, _) =>
        {
            if (++ticks > 40) { fgTimer.Stop(); return; }
            ForceForeground(form.Handle);
            box.Focus();
        };
        fgTimer.Start();

        form.Shown += (_, _) => { ForceForeground(form.Handle); box.Focus(); };

        Application.Run(form);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);

    private static void ForceForeground(IntPtr hwnd)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == hwnd) return;
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint me = GetCurrentThreadId();
            AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            AttachThreadInput(me, fgThread, false);
        }
        catch { /* best-effort */ }
    }
}
