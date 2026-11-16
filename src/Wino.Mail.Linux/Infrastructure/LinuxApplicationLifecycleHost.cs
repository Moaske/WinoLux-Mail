using Wino.Mail.ViewModels.Lifecycle;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// The Linux side of the shared startup and shutdown order (<see cref="ApplicationLifecycleCoordinator"/>): the head's
/// lifecycle hooks in registration order, and the main window's first content.
/// </summary>
public sealed class LinuxApplicationLifecycleHost(ShellWindow window, IEnumerable<IApplicationLifecycleHook> hooks) : IApplicationLifecycleHost
{
    public async Task ServicesReadyAsync()
    {
        foreach (var hook in hooks) await hook.ServicesReadyAsync();
    }

    public async Task StoppingAsync(bool runtimeStarted)
    {
        foreach (var hook in hooks)
        {
            try { await hook.StoppingAsync(runtimeStarted); }
            catch (Exception error) { Serilog.Log.Error(error, "A lifecycle hook failed while stopping."); }
        }
    }

    public Task ShowStartupAsync(ApplicationStartupTarget target)
    {
        window.ShowStartup(target);
        return Task.CompletedTask;
    }

    // The window is already closed when the main loop ended; nothing navigates any more.
    public Task StopNavigationAsync() => Task.CompletedTask;
}
