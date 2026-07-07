using Avalonia.Threading;
using LocalDictation.Application.Abstractions;

namespace LocalDictation.Services;

/// <summary>
/// Marshals work onto the Avalonia UI thread. Required for clipboard access and window manipulation
/// invoked from the background dictation pipeline. Avalonia equivalent of the WPF UiDispatcher.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();

    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
