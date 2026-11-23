using Wino.Core.Domain.Enums;
using Wino.Mail.Linux.Views.Mail;
using Wino.Mail.Linux.Views.Onboarding;
using Wino.Mail.ViewModels.Data;
using Wino.Mail.ViewModels.Navigation;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// Maps Windows page identifiers to GTK pages and the host that shows them (the Mac MacPageRegistry). Each milestone
/// adds its routes; a page without a route is not navigable yet.
/// </summary>
public sealed class LinuxPageRegistry
{
    private readonly Dictionary<WinoPage, (Type Page, NavigationHostKind Host)> _pages = new();
    private readonly Dictionary<WinoPage, Func<object?, NavigationHostKind?>> _hostResolvers = new();

    public LinuxPageRegistry()
    {
        Register<WelcomePage>(WinoPage.WelcomePageV2, NavigationHostKind.Window);
        Register<WelcomePage>(WinoPage.WelcomeHostPage, NavigationHostKind.Window);
        Register<ProviderSelectionPage>(WinoPage.ProviderSelectionPage, NavigationHostKind.Window);
        Register<SpecialImapCredentialsPage>(WinoPage.SpecialImapCredentialsPage, NavigationHostKind.Window);
        Register<AccountSetupProgressPage>(WinoPage.AccountSetupProgressPage, NavigationHostKind.Window);
        Register<MailListPage>(WinoPage.MailListPage, NavigationHostKind.ShellContent);
        Register<MailRenderingPage>(WinoPage.MailRenderingPage, NavigationHostKind.RenderingFrame);
        Register<IdlePage>(WinoPage.IdlePage, NavigationHostKind.RenderingFrame);
        // Editing an account's servers belongs to Settings, which arrives later; adding an account uses the window.
        Register<ImapCalDavSettingsPage>(WinoPage.ImapCalDavSettingsPage, NavigationHostKind.Window);
        _hostResolvers[WinoPage.ImapCalDavSettingsPage] = parameter =>
            parameter is ImapCalDavSettingsNavigationContext { Mode: ImapCalDavSettingsPageMode.Edit } ? NavigationHostKind.Settings : null;
    }

    private void Register<TPage>(WinoPage page, NavigationHostKind host) where TPage : ILinuxPage
        => _pages[page] = (typeof(TPage), host);

    public bool TryGet(WinoPage page, object? parameter, out Type pageType, out NavigationHostKind host)
    {
        if (!_pages.TryGetValue(page, out var route))
        {
            pageType = null!;
            host = default;
            return false;
        }
        pageType = route.Page;
        host = _hostResolvers.TryGetValue(page, out var resolve) && resolve(parameter) is { } resolved ? resolved : route.Host;
        return true;
    }
}
