using Wino.Core.Domain;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>
/// First run (Windows WelcomePageV2): set up an account. The Wino Account import stays hidden (L4), and the JSON import
/// arrives with file picking.
/// </summary>
public sealed class WelcomePage(WelcomePageV2ViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<WelcomePageV2ViewModel>(viewModel, dispatcher, logger)
{
    protected override Gtk.Widget BuildView()
    {
        var start = Form.Command(Translator.WelcomeWindow_GetStartedButton, ViewModel.GetStartedCommand, cssClass: "suggested-action");
        start.AddCssClass("pill");
        start.Halign = Gtk.Align.Center;
        var status = Form.Label(nameof(ViewModel.ImportStatusMessage), vm => vm.ImportStatusMessage, "dim-label");
        status.Halign = Gtk.Align.Center;

#if DEBUG
        LinuxDebugBridge.Register("welcome-start", async _ =>
        {
            await Dispatcher.ExecuteOnUIThread(() => ViewModel.GetStartedCommand.Execute(null));
            return "ok";
        });
#endif
        var page = Adw.StatusPage.New();
        page.IconName = "mail-unread-symbolic";
        page.Title = Translator.WelcomeWindow_SetupTitle;
        page.Description = Translator.WelcomeWindow_SetupSubtitle;
        page.Child = GtkUi.Column(12, start, status);
        page.Vexpand = true;
        return page;
    }
}
