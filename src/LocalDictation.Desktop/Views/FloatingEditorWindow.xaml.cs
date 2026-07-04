using System.Windows;
using System.Windows.Input;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The always-available fallback shown when text cannot be auto-inserted (non-editable or
/// elevated target). Presents the transcript for the user to edit and copy in one click.
/// </summary>
public partial class FloatingEditorWindow : Window, IFloatingEditor
{
    /// <summary>Creates the editor window.</summary>
    public FloatingEditorWindow() => InitializeComponent();

    /// <inheritdoc />
    public void ShowFor(string text, TargetControl target)
    {
        Editor.Text = text;
        Reason.Text = target.Kind == ControlKind.Sensitive
            ? "blocked on sensitive field"
            : target.IsElevated ? "elevated window" : "couldn't auto-insert";
        Show();
        Activate();
        Editor.Focus();
        Editor.SelectAll();
    }

    private void OnDragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Editor.Text); } catch { /* clipboard busy */ }
        CopyBtn.Content = "Copied ✓";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Reset the button label each time the window is shown.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        CopyBtn.Content = "Copy text";
    }
}
