using System.Windows;
using System.Windows.Input;

namespace LocalDictation.Desktop.Views;

/// <summary>
/// A small themed modal confirm dialog (monochrome app chrome), used in place of the native
/// Windows <see cref="MessageBox"/> so prompts match the rest of the app.
/// </summary>
public partial class ConfirmDialog : Window
{
    /// <summary>Creates the dialog with the given title, message and confirm-button label.</summary>
    /// <param name="title">Bold header line.</param>
    /// <param name="message">Body text (wraps).</param>
    /// <param name="confirmText">Label for the primary (confirm) button, e.g. "Download".</param>
    private ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/> and returns true if the user confirmed.
    /// </summary>
    /// <example><c>if (ConfirmDialog.Show(this, "Download model", "…", "Download")) { … }</c></example>
    public static bool Show(Window owner, string title, string message, string confirmText) =>
        new ConfirmDialog(title, message, confirmText) { Owner = owner }.ShowDialog() == true;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
