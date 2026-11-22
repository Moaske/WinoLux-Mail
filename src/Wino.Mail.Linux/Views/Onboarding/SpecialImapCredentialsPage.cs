using CommunityToolkit.Mvvm.Messaging;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;
using Wino.Messaging.Client.Navigation;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>
/// Sign-in for a catalog provider such as iCloud, Yahoo or Fastmail (Windows SpecialImapCredentialsPage): the
/// provider's setup hint with its help link, then name, address, region and the app password. Continue follows the
/// shared ViewModel's validation.
/// </summary>
public sealed class SpecialImapCredentialsPage(SpecialImapCredentialsPageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<SpecialImapCredentialsPageViewModel>(viewModel, dispatcher, logger)
{
    protected override Gtk.Widget BuildView()
    {
        var vm = ViewModel;
        var title = Form.Label(nameof(vm.ProviderName), s => s.ProviderName);
        var heading = GtkUi.Heading(title, GtkUi.Label(Translator.ProviderSelection_SpecialImap_Subtitle));

        var hint = new InfoBar(InfoBarSeverity.Informational);
        Form.Bind(nameof(vm.SetupHint), s => s.SetupHint, text => hint.Message = text ?? string.Empty);
        Form.Visible(hint.Widget, nameof(vm.IsSetupHintVisible), s => s.IsSetupHintVisible);
        var help = Form.Command(vm.HelpLinkText, vm.OpenAppPasswordHelpCommand, cssClass: "flat");
        help.Halign = Gtk.Align.Start;
        Form.Bind(nameof(vm.HelpLinkText), s => s.HelpLinkText, text => help.Label = text);
        Form.Visible(help, nameof(vm.IsHelpLinkVisible), s => s.IsHelpLinkVisible);

        var fields = Adw.PreferencesGroup.New();
        fields.Add(GtkUi.FieldRow(Translator.ProviderSelection_DisplayNameHeader,
            Form.Text(nameof(vm.DisplayName), s => s.DisplayName, (s, v) => s.DisplayName = v, Translator.ProviderSelection_DisplayNamePlaceholder)));
        var email = Form.Text(nameof(vm.EmailAddress), s => s.EmailAddress, (s, v) => s.EmailAddress = v, Translator.ProviderSelection_EmailPlaceholder);
        email.InputPurpose = Gtk.InputPurpose.Email;
        fields.Add(GtkUi.FieldRow(Translator.ProviderSelection_EmailHeader, email));

        var login = Form.Text(nameof(vm.LoginAddress), s => s.LoginAddress, (s, v) => s.LoginAddress = v, vm.LoginAddressPlaceholder);
        login.InputPurpose = Gtk.InputPurpose.Email;
        Form.Bind(nameof(vm.LoginAddressPlaceholder), s => s.LoginAddressPlaceholder, text => login.PlaceholderText = text ?? string.Empty);
        var loginRow = GtkUi.FieldRow(vm.LoginAddressHeader, login, vm.LoginAddressDescription);
        Form.Bind(nameof(vm.LoginAddressHeader), s => s.LoginAddressHeader, text => loginRow.Title = text ?? string.Empty);
        Form.Bind(nameof(vm.LoginAddressDescription), s => s.LoginAddressDescription, text => loginRow.Subtitle = text ?? string.Empty);
        Form.Visible(loginRow, nameof(vm.IsCustomDomain), s => s.IsCustomDomain);
        fields.Add(loginRow);

        var regionRow = GtkUi.FieldRow(Translator.ProviderSelection_RegionHeader, Gtk.Box.New(Gtk.Orientation.Horizontal, 0), vm.RegionDescription);
        Gtk.DropDown? region = null;
        Form.Bind(nameof(vm.Regions), s => s.Regions, regions =>
        {
            if (region is not null) regionRow.Remove(region);
            region = Form.DropDown((regions ?? []).Select(item => item.DisplayName), nameof(vm.SelectedRegion),
                s => s.Regions.IndexOf(s.SelectedRegion), (s, index) => s.SelectedRegion = s.Regions[index]);
            regionRow.AddSuffix(region);
        });
        Form.Bind(nameof(vm.RegionDescription), s => s.RegionDescription, text => regionRow.Subtitle = text ?? string.Empty);
        Form.Visible(regionRow, nameof(vm.IsRegionSelectionVisible), s => s.IsRegionSelectionVisible);
        fields.Add(regionRow);

        var password = Form.Text(nameof(vm.AppSpecificPassword), s => s.AppSpecificPassword, (s, v) => s.AppSpecificPassword = v, secret: true);
        var passwordRow = GtkUi.FieldRow(vm.PasswordHeader, password);
        Form.Bind(nameof(vm.PasswordHeader), s => s.PasswordHeader, text => passwordRow.Title = text ?? string.Empty);
        Form.Visible(passwordRow, nameof(vm.RequiresAppSpecificPassword), s => s.RequiresAppSpecificPassword);
        fields.Add(passwordRow);

        var customDomain = new InfoBar(InfoBarSeverity.Informational);
        Form.Bind(nameof(vm.CustomDomainMessage), s => s.CustomDomainMessage, text => customDomain.Message = text ?? string.Empty);
        Form.Visible(customDomain.Widget, nameof(vm.IsCustomDomain), s => s.IsCustomDomain);

        var back = Gtk.Button.NewWithLabel(Translator.Buttons_Back);
        void GoBack(Gtk.Button sender, EventArgs args) => WeakReferenceMessenger.Default.Send(new BackBreadcrumNavigationRequested(NavigationTransitionEffect.FromLeft));
        back.OnClicked += GoBack;
        Bindings.Own(new ActionDisposable(() => back.OnClicked -= GoBack));
        var proceed = Form.Command(Translator.ProviderSelection_ContinueButton, vm.ProceedCommand, cssClass: "suggested-action");
        Form.Bind(nameof(vm.CanProceed), s => s.CanProceed, _ => proceed.Sensitive = vm.CanProceed && !vm.ProceedCommand.IsRunning);

        var content = GtkUi.Column(18, heading, hint.Widget, help, fields, customDomain.Widget);
        return GtkUi.Page(content, [back], [proceed], 560);
    }
}
