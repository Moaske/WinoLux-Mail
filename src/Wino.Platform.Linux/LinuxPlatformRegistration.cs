using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wino.Authentication;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Platform;
using Wino.Platform.Linux.Security;
using Wino.Platform.Linux.Services;
using Wino.Services;
using Wino.Services.Dav;
using Wino.Services.Platform;
using Wino.Services.PushRelay;
using Wino.Services.Security;

namespace Wino.Platform.Linux;

/// <summary>
/// Paths and data protection that must exist before any database or credential service is constructed.
/// <see cref="Create"/> provisions the installation key on a verified new installation and creates the folders.
/// </summary>
/// <param name="ApplicationIdentity">The application ID; bound into every protected envelope and the keyring item, so it never changes for an installation.</param>
public sealed record LinuxPlatformBootstrap(string ApplicationIdentity, LinuxPaths Paths,
    ProtectedFileSecureItemStore SecureItems, AesGcmEnvelopeSecretProtector Protector)
{
    public static LinuxPlatformBootstrap Create(string applicationIdentity, LinuxPaths paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationIdentity);
        ArgumentNullException.ThrowIfNull(paths);
        var newInstallation = paths.IsVerifiedNewInstallation;
        // The protection key lives in the Secret Service, never in the secure item store the protector encrypts.
        var protector = new AesGcmEnvelopeSecretProtector(new SecretServiceProtectionKeyProvider(applicationIdentity));
        if (newInstallation) protector.ProvisionNewInstallationKey(true);
        paths.CreateDirectories();
        return new LinuxPlatformBootstrap(applicationIdentity, paths, new ProtectedFileSecureItemStore(paths.SecureItemsRoot, protector), protector);
    }
}

public static class LinuxPlatformRegistration
{
    /// <summary>
    /// Registers the Linux platform services and the shared services the Mac, iOS and Android heads also register. Call
    /// it after <c>RegisterSharedServices</c> and <c>RegisterCoreServices</c>, because it replaces the configuration they
    /// register. Outlook and Gmail sign-in are not registered until WinoLux has its own registrations (L3 in the Linux
    /// design decisions), and Wino Account, push relay and purchases stay off (L4). The head registers the UI thread
    /// dispatcher, clipboard, theme, dialog and navigation services.
    /// </summary>
    public static IServiceCollection AddLinuxPlatformServices(this IServiceCollection services, LinuxPlatformBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(bootstrap);

        var paths = bootstrap.Paths;

        services.AddSingleton(paths);
        services.AddSingleton(bootstrap.SecureItems);
        services.AddSingleton<ISecureItemStore>(bootstrap.SecureItems);
        services.AddSingleton<ISecretProtector>(bootstrap.Protector);
        services.Replace(ServiceDescriptor.Singleton<IApplicationConfiguration>(new LinuxApplicationConfiguration
        {
            ApplicationDataFolderPath = paths.ApplicationDataRoot,
            PublisherSharedFolderPath = Path.Combine(paths.ApplicationDataRoot, ApplicationConfiguration.SharedFolderName),
            ApplicationTempFolderPath = paths.TemporaryRoot,
        }));

        // Shared services with the same list and lifetimes as the Mac, iOS and Android registrations.
        services.AddSingleton<IConfigurationService>(_ => new JsonFileConfigurationService(paths.ConfigurationRoot));
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IApplicationResourceResolver>(provider => new FileSystemApplicationResourceResolver(
            paths.PackagedResourceRoot, provider.GetRequiredService<IApplicationConfiguration>()));
        services.AddSingleton<IFileService, FileSystemFileService>();
        services.AddSingleton<IAccountCredentialPersistence, SecureItemAccountCredentialPersistence>();
        services.AddSingleton<IDavCredentialStore, SecureItemDavCredentialStore>();
        services.AddSingleton<IGoogleTokenStore, SecureItemGoogleTokenStore>();
        services.AddSingleton<IAuthenticatorConfig, MailAuthenticatorConfiguration>();
        services.AddSingleton<IStatePersistanceService, ConfigurationStatePersistenceService>();
        services.AddSingleton<IPushRelayDeviceIdentityStore, SecureItemPushRelayDeviceIdentityStore>();
        services.AddSingleton<IAttachmentPlatformService, LauncherAttachmentPlatformService>();
        services.AddSingleton<IStartupIntegrationService, UnavailableStartupIntegrationService>();
        services.AddSingleton<ISmimeCertificateService, UnavailableSmimeCertificateService>();
        services.AddSingleton<IAppMetadataService>(CreateMetadata(bootstrap.ApplicationIdentity));

        // Linux implementations.
        services.AddSingleton<IExternalLauncher, LinuxExternalLauncher>();
        services.AddSingleton<IReaderRuntimeService, LinuxReaderRuntimeService>();
        services.AddSingleton<IShortcutPlatformService, LinuxShortcutPlatformService>();
        services.AddSingleton<IUserPresenceStateProvider, LinuxUserPresenceStateProvider>();
        services.AddSingleton<ITaskCompletionSound, LinuxTaskCompletionSound>();
        services.AddSingleton<IMicrosoftStoreService, LinuxMicrosoftStoreService>();

        // Each capability turns on with the milestone that implements it (Capabilities in the Linux design decisions).
        services.AddSingleton<IPlatformCapabilities>(new PlatformCapabilities(
            PointerHover: true, MailListDensity: true, ConfigurableSyncIntervals: true));
        return services;
    }

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    /// <summary>The assembly's informational version (without the source revision) is the app version.</summary>
    private static HostAppMetadataService CreateMetadata(string applicationIdentity)
    {
        var version = typeof(LinuxPlatformRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        return new HostAppMetadataService(version, applicationIdentity, IsDebugBuild, null, "Linux");
    }
}
