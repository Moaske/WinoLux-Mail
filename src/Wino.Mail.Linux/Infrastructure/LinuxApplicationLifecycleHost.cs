using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.ViewModels.Lifecycle;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// The Linux side of the shared startup and shutdown order (<see cref="ApplicationLifecycleCoordinator"/>): the head's
/// lifecycle hooks in registration order, the first page (Welcome or the shell) and stopping navigation.
/// </summary>
public sealed class LinuxApplicationLifecycleHost(LinuxNavigationService navigation, IDispatcher dispatcher, IEnumerable<IApplicationLifecycleHook> hooks) : IApplicationLifecycleHost
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

    public Task ShowStartupAsync(ApplicationStartupTarget target) => dispatcher.ExecuteOnUIThread(() =>
        _ = Observe(target == ApplicationStartupTarget.Shell ? navigation.ShowShellAsync() : navigation.NavigateAsync(WinoPage.WelcomePageV2)));

    public Task StopNavigationAsync() => navigation.StopAsync();

    private static async Task Observe(Task task)
    {
        try { await task; }
        catch (Exception error) { Serilog.Log.Error(error, "Showing the first page failed."); }
    }
}
