using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The reading pane's idle page (Windows IdlePage, Mac IdlePageViewController): "No message selected", or the
/// mail-empty and account sign-in states the shared ViewModel reports, each with its action button.
/// </summary>
public sealed class IdlePage(IdlePageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<IdlePageViewModel>(viewModel, dispatcher, logger)
{
    private readonly Adw.StatusPage _status = Adw.StatusPage.New();
    private readonly Gtk.Button _action = Gtk.Button.New();

    protected override Gtk.Widget BuildView()
    {
        _status.Child = GtkUi.Column(12, WinoIcons.Label(WinoIconGlyph.Mail, 20), _action);
        _status.AddCssClass("compact");
        _action.Halign = Gtk.Align.Center;
        _action.AddCssClass("pill");
        _action.AddCssClass("suggested-action");
        _action.OnClicked += (_, _) => InvokeAction();
        Apply();
        return _status;
    }

    protected override Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        Form.Bind(nameof(ViewModel.IsMailEmptyStateVisible), vm => vm.IsMailEmptyStateVisible, _ => Apply());
        Form.Bind(nameof(ViewModel.IsAccountStateVisible), vm => vm.IsAccountStateVisible, _ => Apply());
        Form.Bind(nameof(ViewModel.AccountStateMessage), vm => vm.AccountStateMessage, _ => Apply());
        return base.InitializeAsync(mode, parameter);
    }

    private void Apply()
    {
        string? action = null;
        if (ViewModel.IsMailEmptyStateVisible)
        {
            _status.Title = ViewModel.MailEmptyStateTitle;
            _status.Description = GLib.Functions.MarkupEscapeText(ViewModel.MailEmptyStateMessage, -1);
            action = ViewModel.AddAccountText;
        }
        else if (ViewModel.IsAccountStateVisible)
        {
            _status.Title = ViewModel.AccountStateTitle;
            _status.Description = GLib.Functions.MarkupEscapeText(ViewModel.AccountStateMessage, -1);
            if (ViewModel.IsSignInVisible) action = ViewModel.SignInText;
        }
        else
        {
            _status.Title = Translator.NoMailSelected;
            _status.Description = null;
        }
        _status.RemoveCssClass("dim-label");
        if (action is null) _status.AddCssClass("dim-label");
        _action.Label = action ?? string.Empty;
        _action.Visible = action is not null;
    }

    private async void InvokeAction()
    {
        try
        {
            if (ViewModel.IsMailEmptyStateVisible) ViewModel.AddAccountCommand.Execute(null);
            else if (ViewModel.IsSignInVisible) await ViewModel.SignInCommand.ExecuteAsync(null);
        }
        catch (Exception exception) { ReportError(exception); }
    }
}
