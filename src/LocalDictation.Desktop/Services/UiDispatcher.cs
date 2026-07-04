using System.Windows;
using System.Windows.Threading;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// Marshals work onto the WPF UI (STA) thread. Required for clipboard access and window
/// manipulation invoked from the background dictation pipeline.
/// </summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private Dispatcher Dispatcher => System.Windows.Application.Current.Dispatcher;

    /// <inheritdoc />
    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action).Task;

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.InvokeAsync(func).Task;

    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.BeginInvoke(action);
}
