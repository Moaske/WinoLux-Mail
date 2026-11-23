using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;
using Wino.Core;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Views.Mail;
using Wino.Mail.Linux.Views.Onboarding;
using Wino.Mail.Linux.Views.Shell;
using Wino.Mail.ViewModels;
using Wino.Mail.ViewModels.Lifecycle;
using Wino.Platform.Linux;
using Wino.Platform.Linux.Services;
using Wino.Services;
using Wino.Services.Themes;

namespace Wino.Mail.Linux;

/// <summary>
/// The composition root, in the Android and iOS Composition order: platform bootstrap, shared and core services, the
/// Linux platform, ViewModels, then the head's dispatcher, clipboard, dialogs, theme, lifecycle, navigation and pages.
/// </summary>
internal static class LinuxComposition
{
    public static ServiceProvider Create(LinuxPlatformBootstrap bootstrap, LinuxDispatcher dispatcher, ShellWindow window)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: false);
        });
        services.RegisterSharedServices();
        services.RegisterCoreServices();
        services.AddLinuxPlatformServices(bootstrap);

        services.RegisterWinoViewModels();
        services.RegisterLinuxHead(dispatcher, window);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void RegisterLinuxHead(this IServiceCollection services, LinuxDispatcher dispatcher, ShellWindow window)
    {
        services.AddSingleton(dispatcher);
        services.AddSingleton<IDispatcher>(dispatcher);
        services.AddSingleton<IClipboardService, LinuxClipboardService>();
        services.AddSingleton(window);
        services.AddSingleton<IMailDialogService, LinuxDialogService>();
        services.AddSingleton<IDialogServiceBase>(provider => provider.GetRequiredService<IMailDialogService>());

        // Wino themes; custom themes use the Windows file layout.
        services.AddSingleton(provider => new CustomThemeFileStore(provider.GetRequiredService<IApplicationConfiguration>()));
        services.AddSingleton<LinuxThemeService>();
        services.AddSingleton<INewThemeService>(provider => provider.GetRequiredService<LinuxThemeService>());
        services.TryAddSingleton<IUnderlyingThemeService, LinuxUnderlyingThemeService>();

        services.AddSingleton<ApplicationLifecycleCoordinator>();
        services.AddSingleton<LinuxApplicationLifecycleHost>();

        // Navigation and pages; each page resolves in a scope of its own (PageScopes).
        var registry = new LinuxPageRegistry();
        services.AddSingleton(registry);
        services.AddSingleton<LinuxNavigationService>();
        services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<LinuxNavigationService>());
        services.AddTransient<WelcomePage>();
        services.AddTransient<ProviderSelectionPage>();
        services.AddTransient<SpecialImapCredentialsPage>();
        services.AddTransient<ImapCalDavSettingsPage>();
        services.AddTransient<AccountSetupProgressPage>();
        services.AddTransient<WinoAppShellPage>();
        services.AddTransient<MailListPage>();
        services.AddTransient<MailRenderingPage>();
        services.AddTransient<IdlePage>();
        services.AddSingleton<LinuxSenderPictureProvider>();
        services.AddSingleton<IApplicationLifecycleHook, LinuxSenderPictureRecoveryHook>();
    }
}
