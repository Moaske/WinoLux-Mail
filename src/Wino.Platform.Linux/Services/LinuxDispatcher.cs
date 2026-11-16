using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Services;

/// <summary>
/// The UI thread is the GLib main loop's thread. The head creates the dispatcher on that thread while the loop runs, so
/// <paramref name="context"/> is Gir.Core's GLib synchronization context. Work already on the UI thread runs inline (the
/// AppKit, UIKit and Android dispatcher contract), so a caller on the UI thread never waits for its own queue.
/// After the main loop ended (<see cref="MainLoopEnded"/>), work runs inline: the shutdown order runs then, with no
/// window left and no loop to post to.
/// </summary>
public sealed class LinuxDispatcher(SynchronizationContext context) : IDispatcher
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private volatile bool _mainLoopEnded;

    /// <summary>Called by the head once the GLib main loop returned.</summary>
    public void MainLoopEnded() => _mainLoopEnded = true;

    public Task ExecuteOnUIThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_mainLoopEnded || Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }, null);
        return completion.Task;
    }
}
