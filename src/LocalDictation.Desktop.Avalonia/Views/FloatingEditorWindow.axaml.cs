using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;

namespace LocalDictation.Views;

/// <summary>
/// The always-available fallback shown when text can't (or shouldn't) be auto-inserted — the target
/// lost focus, is a sensitive/elevated field, or every insertion strategy failed. Presents the
/// transcript in an editable window (native macOS chrome) so the user can edit and copy it. Avalonia
/// port of the WPF FloatingEditorWindow.
/// </summary>
public partial class FloatingEditorWindow : Window, IFloatingEditor
{
    private TextBox _editor = null!;
    private TextBlock _reason = null!;
    private Button _copyBtn = null!;

    /// <summary>Creates the editor window.</summary>
    public FloatingEditorWindow()
    {
        InitializeComponent();
        _editor = this.FindControl<TextBox>("Editor")!;
        _reason = this.FindControl<TextBlock>("Reason")!;
        _copyBtn = this.FindControl<Button>("CopyBtn")!;

        // Hide (not close) on the window's close button so the singleton instance survives.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public void ShowFor(string text, TargetControl target, EditorReason reason) => Dispatcher.UIThread.Post(() =>
    {
        _editor.Text = text;
        _reason.Text = reason switch
        {
            EditorReason.FocusMoved => "focus moved — not inserted",
            EditorReason.Sensitive => "blocked on a sensitive field",
            EditorReason.Elevated => "target is an elevated window",
            _ => "couldn't auto-insert",
        };
        _copyBtn.Content = "Copy text";
        Show();
        Activate();
        _editor.Focus();
        _editor.SelectAll();
    });

    private void OnClose(object? sender, RoutedEventArgs e) => Hide();

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/pbcopy") { RedirectStandardInput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                p.StandardInput.Write(_editor.Text ?? "");
                p.StandardInput.Close();
                p.WaitForExit(2000);
            }
        }
        catch { /* clipboard busy */ }
        _copyBtn.Content = "Copied ✓";
    }
}
