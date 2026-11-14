using Microsoft.Extensions.DependencyInjection;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.ViewModels.Lifecycle;
using Wino.Platform.Linux;
using Wino.Platform.Linux.Services;

namespace Wino.Mail.Linux;

/// <summary>
/// Starts the GTK application. The first activation shows the main window, builds the service container on the UI
/// thread (where Gir.Core's GLib synchronization context runs) and runs the shared startup order. When the last window
/// closed and the main loop returned, the shared shutdown order runs and the container is disposed.
/// </summary>
public static class Program
{
    public const string ApplicationId = "io.github.moaske.WinoLux";

    private static ServiceProvider? _services;
    private static LinuxDispatcher? _dispatcher;
    private static ShellWindow? _window;

    public static int Main()
    {
        Adw.Module.Initialize();

        var application = Adw.Application.New(ApplicationId, Gio.ApplicationFlags.DefaultFlags);
        application.OnActivate += (_, _) => Activate(application);

        var exitCode = application.RunWithSynchronizationContext(null);

        _dispatcher?.MainLoopEnded();
        if (_services is { } services)
        {
            try
            {
                Task.Run(() => services.GetRequiredService<ApplicationLifecycleCoordinator>()
                    .StopAsync(services.GetRequiredService<LinuxApplicationLifecycleHost>())).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Serilog.Log.Error(error, "Stopping the application failed.");
            }
            finally
            {
                Serilog.Log.CloseAndFlush();
                services.Dispose();
            }
        }
        return exitCode;
    }

    private static void Activate(Adw.Application application)
    {
        if (_window is { } existing)
        {
            existing.Present();
            return;
        }

        var window = new ShellWindow(application);
        _window = window;
        window.Present();

        try
        {
            var bootstrap = LinuxPlatformBootstrap.Create(ApplicationId, LinuxPaths.FromEnvironment());
            _dispatcher = new LinuxDispatcher(SynchronizationContext.Current
                ?? throw new InvalidOperationException("The GLib synchronization context is not installed."));
            _services = LinuxComposition.Create(bootstrap, _dispatcher, window);
        }
        catch (Exception error) when (error is PlatformNotSupportedException or System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            window.ShowStartupFailure(error);
            return;
        }

        _ = StartAsync(_services, window);
    }

    private static async Task StartAsync(ServiceProvider services, ShellWindow window)
    {
        try
        {
            await services.GetRequiredService<ApplicationLifecycleCoordinator>().StartAsync(services.GetRequiredService<LinuxApplicationLifecycleHost>());
        }
        catch (Exception error)
        {
            Serilog.Log.Fatal(error, "Starting the application failed.");
            window.ShowStartupFailure(error);
        }
    }
}
