using System;
using System.Windows;
using System.Windows.Input;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// The always-available fallback shown when text can't (or shouldn't) be auto-inserted — the target
/// lost focus, is a sensitive/elevated field, or every insertion strategy failed. Presents the
/// transcript in a translucent dark-glass window with standard min/maximize/close chrome so the user
/// can edit and copy it.
/// </summary>
public partial class FloatingEditorWindow : Window, IFloatingEditor
{
    // Segoe Fluent Icons glyphs, built from code points so the source stays ASCII-clean.
    private static readonly string MaximizeGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private Rect _restoreBounds;
    private bool _maximized;

    /// <summary>Creates the editor window.</summary>
    public FloatingEditorWindow() => InitializeComponent();

    /// <inheritdoc />
    public void ShowFor(string text, TargetControl target, EditorReason reason)
    {
        Editor.Text = text;
        Reason.Text = reason switch
        {
            EditorReason.FocusMoved => "focus moved — not inserted",
            EditorReason.Sensitive => "blocked on a sensitive field",
            EditorReason.Elevated => "target is an elevated window",
            _ => "couldn't auto-insert",
        };
        CopyBtn.Content = "Copy text";
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Editor.Focus();
        Editor.SelectAll();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; } // double-click title bar = maximize/restore
        DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnCloseButton(object sender, RoutedEventArgs e) => Hide();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Editor.Text); } catch { /* clipboard busy */ }
        CopyBtn.Content = "Copied ✓";
    }

    /// <summary>
    /// Toggles between the default size and filling the working area using manual work-area bounds
    /// (not <see cref="WindowState.Maximized"/>) so this borderless, transparent window doesn't cover
    /// the taskbar or clip its rounded corners.
    /// </summary>
    private void ToggleMaximize()
    {
        if (!_maximized)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
            _maximized = true;
            MaxButton.Content = RestoreGlyph;
            MaxButton.ToolTip = "Restore";
        }
        else
        {
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            _maximized = false;
            MaxButton.Content = MaximizeGlyph;
            MaxButton.ToolTip = "Maximize";
        }
    }
}
