using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Wino.Core.Domain.Models.Navigation;
using Wino.Core.Domain.Interfaces;
using Wino.Core.ViewModels;
using Wino.Mail.Linux.Presentation;
using Wino.Presentation.Bindings;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>A routed page: the navigation host shows <see cref="Widget"/>, then activates the page; it releases it when it leaves.</summary>
public interface ILinuxPage
{
    /// <summary>The page's widget, built on the UI thread the first time it is read.</summary>
    Gtk.Widget Widget { get; }
    bool HasPendingWork { get; }
    object AssociatedViewModel { get; }
    Task ActivateAsync(NavigationMode mode, object? parameter);
    Task ReleaseAsync();
}

/// <summary>
/// Base of every GTK page, with the Mac WinoViewController lifecycle: the router hosts the widget, then
/// <see cref="ActivateAsync"/> runs <see cref="InitializeAsync"/> (OnNavigatedTo) and OnPageLoaded, and
/// <see cref="ReleaseAsync"/> deactivates the ViewModel and disposes the bindings and the page's service scope.
/// </summary>
public abstract class LinuxPage<TViewModel> : ILinuxPage where TViewModel : CoreBaseViewModel
{
    private Gtk.Widget? _widget;
    private bool _active;
    private bool _released;
    private Task _initialization = Task.CompletedTask;

    protected LinuxPage(TViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    {
        ViewModel = viewModel;
        Dispatcher = dispatcher;
        Logger = logger;
        ViewModel.Dispatcher = dispatcher;
        Form = new GtkForm<TViewModel>(viewModel, Bindings, dispatcher, ReportError);
    }

    protected TViewModel ViewModel { get; }
    protected IDispatcher Dispatcher { get; }
    protected IWinoLogger Logger { get; }
    protected BindingScope Bindings { get; } = new();
    protected GtkForm<TViewModel> Form { get; }

    public Gtk.Widget Widget => _widget ??= BuildView();
    public object AssociatedViewModel => ViewModel;
    public virtual bool HasPendingWork => !_initialization.IsCompleted;

    /// <summary>Builds the page's widget tree and its bindings. UI thread.</summary>
    protected abstract Gtk.Widget BuildView();

    public async Task ActivateAsync(NavigationMode mode, object? parameter)
    {
        ObjectDisposedException.ThrowIf(_released, this);
        if (_active) return;
        _active = true;
        await Dispatcher.ExecuteOnUIThread(() =>
        {
            _ = Widget;
            _initialization = InitializeAsync(mode, parameter);
        });
        await _initialization;
        await Dispatcher.ExecuteOnUIThread(() => { if (!_released) ViewModel.OnPageLoaded(); });
    }

    protected virtual Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        ViewModel.OnNavigatedTo(mode, parameter!);
        return Task.CompletedTask;
    }

    protected virtual Task DeactivateAsync()
    {
        ViewModel.OnNavigatedFrom(NavigationMode.New, null!);
        return Task.CompletedTask;
    }

    protected void ReportError(Exception exception) => Logger.CaptureException(exception, GetType().Name);

    public async Task ReleaseAsync()
    {
        if (_released) return;
        _released = true;
        try { await _initialization; }
        catch (Exception exception) { ReportError(exception); }
        var deactivation = Task.CompletedTask;
        await Dispatcher.ExecuteOnUIThread(() =>
        {
            if (_active) deactivation = DeactivateAsync();
            _active = false;
            Bindings.Dispose();
        });
        try { await deactivation; }
        finally
        {
            await Dispatcher.ExecuteOnUIThread(() => ViewModel.Dispatcher = null!);
            PageScopes.Release(this);
        }
    }
}

/// <summary>
/// Resolves pages in a service scope of their own (the UIKit PageScopes), so a page's transient disposables are released
/// with the page instead of staying rooted in the application container.
/// </summary>
public static class PageScopes
{
    private static readonly ConditionalWeakTable<object, IServiceScope> Scopes = new();

    public static object Resolve(IServiceProvider services, Type type)
    {
        var scope = services.CreateScope();
        var instance = scope.ServiceProvider.GetRequiredService(type);
        Scopes.AddOrUpdate(instance, scope);
        return instance;
    }

    public static void Release(object instance)
    {
        if (!Scopes.TryGetValue(instance, out var scope)) return;
        Scopes.Remove(instance);
        scope.Dispose();
    }
}
