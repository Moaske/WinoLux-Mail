using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Views.Shell;
using Wino.Mail.ViewModels.Navigation;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// The GTK host of the shared <see cref="NavigationCoordinator"/>, which owns routes, history, back confirmation and the
/// account lifecycle reactions (the Mac AppKitNavigationService's twin). This class only creates, shows, activates and
/// releases pages: the main window shows one page at a time (onboarding pages, or the <see cref="WinoAppShellPage"/>),
/// and the shell shows the mode's content page. Routes come from <see cref="LinuxPageRegistry"/>. Rendering frames,
/// the secondary onboarding window and Settings arrive with later milestones.
/// </summary>
public sealed class LinuxNavigationService : INavigationService, INavigationHost, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IDispatcher _dispatcher;
    private readonly ShellWindow _window;
    private readonly LinuxPageRegistry _registry;
    private readonly NavigationCoordinator _coordinator;
    private ILinuxPage? _current;
    private WinoAppShellPage? _shell;

    /// <summary>The page in the window, by type name (the debug bridge reports it).</summary>
    internal string? CurrentPageName => _current?.GetType().Name;

    public LinuxNavigationService(IServiceProvider services, IDispatcher dispatcher, ShellWindow window, LinuxPageRegistry registry)
    {
        _services = services;
        _dispatcher = dispatcher;
        _window = window;
        _registry = registry;
        _coordinator = new NavigationCoordinator(this, services, dispatcher);
    }

    /// <summary>The shell while it is in the window.</summary>
    public WinoAppShellPage? Shell => _shell;

    public bool CanQuit => _coordinator.CanQuit;
    public bool IsModeAvailable(WinoApplicationMode mode) => _coordinator.IsModeAvailable(mode);
    public bool IsShellReady => _coordinator.IsShellReady;

    public Type? GetPageType(WinoPage page) => _registry.TryGet(page, null, out var type, out _) ? type : null;

    public bool Navigate(WinoPage page, object? parameter = null, NavigationReferenceFrame? frame = null, NavigationTransitionType transition = NavigationTransitionType.None)
        => _coordinator.Navigate(page, parameter, frame, transition);

    public Task NavigateAsync(WinoPage page, object? parameter = null) => _coordinator.NavigateAsync(page, parameter);
    public Task ShowShellAsync() => _coordinator.ShowShellAsync();
    public bool ChangeApplicationMode(WinoApplicationMode mode) => _coordinator.ChangeApplicationMode(mode);
    public bool ChangeApplicationMode(WinoApplicationMode mode, ShellModeActivationContext activationContext) => _coordinator.ChangeApplicationMode(mode, activationContext);
    public bool ParkShell() => _coordinator.ParkShell();
    public bool RestoreShell(WinoApplicationMode mode) => _coordinator.RestoreShell(mode);
    public bool RestoreShell(WinoApplicationMode mode, ShellModeActivationContext activationContext) => _coordinator.RestoreShell(mode, activationContext);
    public bool CanGoBack() => _coordinator.CanGoBack();
    public void GoBack(NavigationTransitionEffect slideEffect = NavigationTransitionEffect.FromRight) => _coordinator.GoBack(slideEffect);
    public Task<bool> GoBackAsync(NavigationTransitionEffect slideEffect = NavigationTransitionEffect.FromRight) => _coordinator.GoBackAsync(slideEffect);
    public void SetNavigationResult(NavigationResult result) => _coordinator.SetNavigationResult(result);
    public Task StopAsync() => _coordinator.StopAsync();
    public void Dispose() => _coordinator.Dispose();

    #region INavigationHost

    bool INavigationHost.TryGetRoute(WinoPage page, object? parameter, out NavigationHostKind hostKind) => _registry.TryGet(page, parameter, out _, out hostKind);

    bool INavigationHost.IsShellShown => _shell is not null;
    bool INavigationHost.HasMainContent => _current is not null;
    bool INavigationHost.MainContentHasPendingWork => _current is { HasPendingWork: true };
    object? INavigationHost.ShellContentViewModel => _shell?.Content?.AssociatedViewModel;
    bool INavigationHost.IsSecondaryWindowShown => false;
    bool INavigationHost.IsSettingsAvailable => false;

    Task INavigationHost.ShowInRenderingFrameAsync(WinoPage page, object? parameter) => Task.CompletedTask;

    async Task INavigationHost.SetShellContentAsync(WinoPage page, object? parameter)
    {
        if (_shell is not { } shell) return;
        var next = await CreatePageAsync(page, parameter);
        await shell.SetContentAsync(next, parameter);
    }

    Task INavigationHost.ClearShellContentAsync() => _shell?.ClearContentAsync() ?? Task.CompletedTask;

    async Task INavigationHost.ReplaceMainContentAsync(WinoPage page, object? parameter, NavigationMode mode, Action committed)
    {
        var next = await CreatePageAsync(page, parameter);
        await ShowAsync(next, committed);
        await next.ActivateAsync(mode, parameter);
    }

    /// <summary>Settings › Add account opens with Settings in milestone 2c; until then the main window is the only surface.</summary>
    async Task INavigationHost.ShowInSecondaryWindowAsync(WinoPage page, object? parameter, NavigationMode mode, Action committed)
    {
        var next = await CreatePageAsync(page, parameter);
        await ShowAsync(next, committed);
        await next.ActivateAsync(mode, parameter);
    }

    Task INavigationHost.CloseSecondaryWindowAsync(bool closedByUser) => Task.CompletedTask;

    async Task<Func<Task>> INavigationHost.PresentShellAsync(Action committed)
    {
        WinoAppShellPage? created = null;
        await _dispatcher.ExecuteOnUIThread(() => created = (WinoAppShellPage)PageScopes.Resolve(_services, typeof(WinoAppShellPage)));
        var shell = created ?? throw new InvalidOperationException("The app shell could not be created.");
        await ShowAsync(shell, committed, hasOwnHeaderBars: true);
        _shell = shell;
        return () => shell.ActivateAsync(NavigationMode.New, null);
    }

    Task INavigationHost.BringShellToFrontAsync() => _dispatcher.ExecuteOnUIThread(_window.Present);
    Task INavigationHost.ActivateShellModeAsync(WinoApplicationMode mode, ShellModeActivationContext? activationContext)
        => _shell?.ActivateModeAsync(mode, activationContext) ?? Task.CompletedTask;
    Task INavigationHost.DismissShellSurfacesAsync() => Task.CompletedTask;

    async Task INavigationHost.ReleaseMainContentAsync()
    {
        var current = _current;
        _current = null;
        _shell = null;
        if (current is not null) await current.ReleaseAsync();
    }

    Task INavigationHost.ShowSettingsAsync(WinoPage? page, object? parameter) => Task.CompletedTask;
    bool INavigationHost.TrySettingsGoBack() => false;
    Task INavigationHost.CloseSettingsAsync() => Task.CompletedTask;
    Task INavigationHost.StopSettingsAsync() => Task.CompletedTask;

    #endregion

    /// <summary>Puts <paramref name="next"/> in the window, then releases the previous page (the shell included).</summary>
    private async Task ShowAsync(ILinuxPage next, Action committed, bool hasOwnHeaderBars = false)
    {
        var previous = _current;
        try { await _dispatcher.ExecuteOnUIThread(() => _window.ShowPage(next.Widget, hasOwnHeaderBars)); }
        catch
        {
            await next.ReleaseAsync();
            throw;
        }
        committed();
        _current = next;
        if (ReferenceEquals(previous, _shell)) _shell = null;
        if (previous is not null) await previous.ReleaseAsync();
    }

    private async Task<ILinuxPage> CreatePageAsync(WinoPage page, object? parameter)
    {
        if (!_registry.TryGet(page, parameter, out var type, out _))
            throw new NotSupportedException($"The page {page} has no Linux route yet.");
        ILinuxPage? created = null;
        await _dispatcher.ExecuteOnUIThread(() => created = (ILinuxPage)PageScopes.Resolve(_services, type));
        return created ?? throw new InvalidOperationException($"The page {page} produced no widget.");
    }
}
