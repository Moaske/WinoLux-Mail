using Wino.Core.Domain;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;
using Wino.Mail.ViewModels.Data;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>
/// Custom IMAP / SMTP (and CalDAV / CardDAV) server page (Windows ImapCalDavSettingsPage, Mac
/// ImapCalDavSettingsPageViewController). Adding an account it is a guided flow: sign in, then the discovered servers
/// in expandable sections with a connection test. Editing an account's servers arrives with Settings.
/// </summary>
public sealed class ImapCalDavSettingsPage(ImapCalDavSettingsPageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<ImapCalDavSettingsPageViewModel>(viewModel, dispatcher, logger)
{
    protected override Gtk.Widget BuildView()
    {
        var vm = ViewModel;
        var step = Form.Label(nameof(vm.CurrentSetupStep), s => s.IsCreateMode
            ? string.Format(Translator.ProviderSelection_StepProgressOf, (int)s.CurrentSetupStep + 1, 2) : string.Empty, "dim-label", "caption");
        var title = GtkUi.Label(string.Empty);
        var description = GtkUi.Label(string.Empty);
        void UpdateHeader()
        {
            title.SetText(vm.IsSignInStepVisible ? vm.SignInTitle : vm.IsEditMode ? Translator.ImapCalDavSettingsPage_TitleEdit : Translator.ImapCalDavSettingsPage_TitleCreate);
            description.SetText(vm.IsSignInStepVisible ? vm.SignInDescription : string.Empty);
            description.Visible = vm.IsSignInStepVisible;
        }
        foreach (var property in new[] { nameof(vm.IsSignInStepVisible), nameof(vm.SignInTitle), nameof(vm.SignInDescription), nameof(vm.IsEditMode) })
            Form.Bind(property, _ => 0, _ => UpdateHeader());

        var pageError = new InfoBar(InfoBarSeverity.Error);
        Form.Bind(nameof(vm.PageInfoBarTitle), s => s.PageInfoBarTitle, text => pageError.Title = text);
        Form.Bind(nameof(vm.PageInfoBarMessage), s => s.PageInfoBarMessage, text => pageError.Message = text);
        Form.Visible(pageError.Widget, nameof(vm.IsPageInfoBarOpen), s => s.IsPageInfoBarOpen);
        var pop3 = new InfoBar(InfoBarSeverity.Informational, Translator.POP3Setup_LimitationsTitle, Translator.POP3Setup_LimitationsMessage);
        Form.Visible(pop3.Widget, nameof(vm.IsPop3), s => s.IsPop3);

        var signIn = Adw.PreferencesGroup.New();
        AddSignInRows(signIn);
        Form.Visible(signIn, nameof(vm.IsSignInStepVisible), s => s.IsSignInStepVisible);

        var spinner = Adw.Spinner.New();
        var busyText = Form.Label(nameof(vm.BusyText), s => s.BusyText, "dim-label");
        var busy = GtkUi.Row(8, spinner, busyText);
        Form.Visible(busy, nameof(vm.IsBusy), s => s.IsBusy);
        var back = Form.Command(Translator.Buttons_Back, vm.BackCommand);
        Form.Visible(back, nameof(vm.IsCreateMode), s => s.IsCreateMode);
        var primary = Form.Command(Translator.ProviderSelection_ContinueButton, vm.PrimaryActionCommand, cssClass: "suggested-action");
        Form.Bind(nameof(vm.PrimaryActionText), s => s.PrimaryActionText, text => primary.Label = text);

        var content = GtkUi.Column(18, GtkUi.Column(6, step, GtkUi.Heading(title, description)), pageError.Widget, pop3.Widget, signIn, BuildServers());
#if DEBUG
        RegisterDebugCommands();
#endif
        return GtkUi.Page(content, [back, busy], [primary]);
    }

#if DEBUG
    /// <summary>
    /// Debug bridge: <c>imap-identity NAME ADDRESS</c> (sender name and address), <c>imap-incoming HOST PORT SECURITY AUTH</c>
    /// and <c>imap-outgoing HOST PORT SECURITY AUTH</c> (server, port and drop-down indexes), <c>imap-continue</c> (the
    /// primary action) and <c>imap-state</c>. No credential is ever filled: a test focuses <c>imap-password</c> and types.
    /// </summary>
    private void RegisterDebugCommands()
    {
        LinuxDebugBridge.Register("imap-identity", args => OnUi(() =>
        {
            if (args.Length < 2) return "usage: imap-identity NAME ADDRESS";
            ViewModel.DisplayName = string.Join(' ', args[..^1]);
            ViewModel.EmailAddress = args[^1];
            return "ok";
        }));
        LinuxDebugBridge.Register("imap-incoming", args => OnUi(() =>
        {
            if (args.Length < 4 || !int.TryParse(args[2], out var security) || !int.TryParse(args[3], out var auth)) return "usage: imap-incoming HOST PORT SECURITY AUTH";
            ViewModel.IncomingServer = args[0];
            ViewModel.IncomingServerPort = args[1];
            ViewModel.SelectedIncomingServerConnectionSecurityIndex = security;
            ViewModel.SelectedIncomingServerAuthenticationMethodIndex = auth;
            return "ok " + ViewModel.IncomingSummary;
        }));
        LinuxDebugBridge.Register("imap-outgoing", args => OnUi(() =>
        {
            if (args.Length < 4 || !int.TryParse(args[2], out var security) || !int.TryParse(args[3], out var auth)) return "usage: imap-outgoing HOST PORT SECURITY AUTH";
            ViewModel.OutgoingServer = args[0];
            ViewModel.OutgoingServerPort = args[1];
            ViewModel.SelectedOutgoingServerConnectionSecurityIndex = security;
            ViewModel.SelectedOutgoingServerAuthenticationMethodIndex = auth;
            return "ok " + ViewModel.OutgoingSummary;
        }));
        LinuxDebugBridge.Register("imap-continue", async _ =>
        {
            var canRun = false;
            await Dispatcher.ExecuteOnUIThread(() => canRun = ViewModel.PrimaryActionCommand.CanExecute(null));
            if (!canRun) return "cannot continue";
            await ViewModel.PrimaryActionCommand.ExecuteAsync(null);
            return "ok step=" + ViewModel.CurrentSetupStep;
        });
        LinuxDebugBridge.Register("imap-state", _ => OnUi(() =>
            $"step={ViewModel.CurrentSetupStep} busy={ViewModel.IsBusy} mail={ViewModel.MailConnectionState} error={ViewModel.MailConnectionError} page={ViewModel.PageInfoBarMessage} " +
            $"security=[{string.Join('|', ViewModel.AvailableConnectionSecurityDisplayNames)}] auth=[{string.Join('|', ViewModel.AvailableAuthenticationMethodDisplayNames)}]"));
    }

    private async Task<string> OnUi(Func<string> action)
    {
        var result = string.Empty;
        await Dispatcher.ExecuteOnUIThread(() => result = action());
        return result;
    }
#endif

    /// <summary>Who is signing in: name, address, the CalDAV address when there is no mail, the password and its help.</summary>
    private void AddSignInRows(Adw.PreferencesGroup group)
    {
        var vm = ViewModel;
        var name = GtkUi.FieldRow(Translator.ImapSetup_YourName,
            Form.Text(nameof(vm.DisplayName), s => s.DisplayName, (s, v) => s.DisplayName = v, Translator.IMAPSetupDialog_DisplayNamePlaceholder),
            Translator.ImapSetup_YourNameDescription);
        Form.Visible(name, nameof(vm.IsSignInSenderNameVisible), s => s.IsSignInSenderNameVisible);
        group.Add(name);

        var address = Form.Text(nameof(vm.EmailAddress), s => s.EmailAddress, (s, v) => s.EmailAddress = v, Translator.IMAPSetupDialog_MailAddressPlaceholder);
        address.InputPurpose = Gtk.InputPurpose.Email;
        var addressRow = GtkUi.FieldRow(vm.SignInAddressHeader, address);
        Form.Bind(nameof(vm.SignInAddressHeader), s => s.SignInAddressHeader, text => addressRow.Title = text);
        group.Add(addressRow);

        var server = GtkUi.FieldRow(Translator.ImapSetup_ServerAddressOptional,
            Form.Text(nameof(vm.CalDavServiceUrl), s => s.CalDavServiceUrl, (s, v) => s.CalDavServiceUrl = v, Translator.ImapSetup_ServerAddressPlaceholder));
        Form.Visible(server, nameof(vm.IsSignInServerAddressVisible), s => s.IsSignInServerAddressVisible);
        group.Add(server);

        var passwordEntry = Form.Text(nameof(vm.Password), s => s.Password, (s, v) => s.Password = v, secret: true);
#if DEBUG
        LinuxDebugBridge.RegisterFocus("imap-password", passwordEntry);
#endif
        var password = GtkUi.FieldRow(Translator.IMAPSetupDialog_Password, passwordEntry);
        Form.Bind(nameof(vm.AppPasswordHelpText), s => s.AppPasswordHelpText, text => password.Subtitle = text ?? string.Empty);
        group.Add(password);

        var help = Form.Command(Translator.ImapSetup_AppPasswordHelpLink, vm.OpenAppPasswordHelpCommand, cssClass: "flat");
        help.Halign = Gtk.Align.Start;
        help.MarginTop = 6;
        Form.Visible(help, nameof(vm.HasAppPasswordHelpLink), s => s.HasAppPasswordHelpLink);
        group.Add(help);
    }

    #region Servers

    private Gtk.Widget BuildServers()
    {
        var vm = ViewModel;
        var found = new InfoBar(InfoBarSeverity.Success);
        Form.Bind(nameof(vm.DiscoveryFoundMessage), s => s.DiscoveryFoundMessage, text => found.Message = text);
        Form.Visible(found.Widget, nameof(vm.IsDiscoveryFoundVisible), s => s.IsDiscoveryFoundVisible);
        var notFound = new InfoBar(InfoBarSeverity.Warning);
        Form.Bind(nameof(vm.DiscoveryNotFoundMessage), s => s.DiscoveryNotFoundMessage, text => notFound.Message = text);
        Form.Visible(notFound.Widget, nameof(vm.IsDiscoveryNotFoundVisible), s => s.IsDiscoveryNotFoundVisible);

        var group = Adw.PreferencesGroup.New();
        group.Title = Translator.ImapSetup_ConnectionsGroup;
        group.HeaderSuffix = Form.Command(Translator.ImapSetup_TestConnections, vm.TestConnectionsCommand);
        group.Add(BuildIncoming());
        group.Add(BuildOutgoing());
        group.Add(BuildCalDav());
        group.Add(BuildCardDav());
        group.Add(BuildAdvanced());

        var mailError = new InfoBar(InfoBarSeverity.Error, Translator.IMAPSetupDialog_ValidationFailed_Title);
        Form.Bind(nameof(vm.MailConnectionError), s => s.MailConnectionError, text => mailError.Message = text);
        Form.Visible(mailError.Widget, nameof(vm.HasMailConnectionError), s => s.HasMailConnectionError);
        var davError = new InfoBar(InfoBarSeverity.Error, Translator.IMAPSetupDialog_ValidationFailed_Title);
        Form.Bind(nameof(vm.CalDavConnectionError), s => s.CalDavConnectionError, text => davError.Message = text);
        Form.Visible(davError.Widget, nameof(vm.HasCalDavConnectionError), s => s.HasCalDavConnectionError);

        var servers = GtkUi.Column(12, found.Widget, notFound.Widget, mailError.Widget, davError.Widget, group);
        Form.Visible(servers, nameof(vm.IsServersStepVisible), s => s.IsServersStepVisible);
        return servers;
    }

    private Adw.ExpanderRow Expander(string title, string expandedProperty, Func<ImapCalDavSettingsPageViewModel, bool> read, Action<ImapCalDavSettingsPageViewModel, bool> write)
    {
        var expander = Adw.ExpanderRow.New();
        expander.Title = title;
        var binding = Form.Bind(expandedProperty, read, expanded => { if (expander.Expanded != expanded) expander.Expanded = expanded; }, write);
        void Changed(GObject.Object sender, GObject.Object.NotifySignalArgs args)
        {
            if (args.Pspec.GetName() == "expanded") binding.UpdateSource(expander.Expanded);
        }
        expander.OnNotify += Changed;
        Bindings.Own(new ActionDisposable(() => expander.OnNotify -= Changed));
        return expander;
    }

    private static Gtk.Image StatusIcon() => Gtk.Image.New();

    private static void ShowState(Gtk.Image icon, ConnectionTestState state)
    {
        icon.Visible = state != ConnectionTestState.NotTested;
        icon.SetFromIconName(state switch
        {
            ConnectionTestState.Succeeded => "emblem-ok-symbolic",
            ConnectionTestState.Failed => "dialog-error-symbolic",
            _ => "content-loading-symbolic"
        });
    }

    private Gtk.Widget BuildIncoming()
    {
        var vm = ViewModel;
        var expander = Expander(vm.IncomingSettingsTitle, nameof(vm.IsIncomingExpanded), s => s.IsIncomingExpanded, (s, v) => s.IsIncomingExpanded = v);
        Form.Bind(nameof(vm.IncomingSettingsTitle), s => s.IncomingSettingsTitle, text => expander.Title = text);
        Form.Bind(nameof(vm.IncomingSummary), s => s.IncomingSummary, text => expander.Subtitle = text ?? string.Empty);
        var status = StatusIcon();
        Form.Bind(nameof(vm.MailConnectionState), s => s.MailConnectionState, state => ShowState(status, state));
        expander.AddSuffix(status);
        Form.Visible(expander, nameof(vm.IsMailSupportEnabled), s => s.IsMailSupportEnabled);

        expander.AddRow(GtkUi.FieldRow(Translator.ImapSetup_ServerHeader,
            Form.Text(nameof(vm.IncomingServer), s => s.IncomingServer, (s, v) => s.IncomingServer = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.IMAPSetupDialog_IncomingMailServerPort,
            Form.Text(nameof(vm.IncomingServerPort), s => s.IncomingServerPort, (s, v) => s.IncomingServerPort = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_ConnectionSecurityHeader,
            Form.DropDown(vm.AvailableConnectionSecurityDisplayNames, nameof(vm.SelectedIncomingServerConnectionSecurityIndex),
                s => s.SelectedIncomingServerConnectionSecurityIndex, (s, v) => s.SelectedIncomingServerConnectionSecurityIndex = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_AuthenticationMethodHeader,
            Form.DropDown(vm.AvailableAuthenticationMethodDisplayNames, nameof(vm.SelectedIncomingServerAuthenticationMethodIndex),
                s => s.SelectedIncomingServerAuthenticationMethodIndex, (s, v) => s.SelectedIncomingServerAuthenticationMethodIndex = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.IMAPSetupDialog_Username,
            Form.Text(nameof(vm.IncomingServerUsername), s => s.IncomingServerUsername, (s, v) => s.IncomingServerUsername = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.IMAPSetupDialog_Password,
            Form.Text(nameof(vm.IncomingServerPassword), s => s.IncomingServerPassword, (s, v) => s.IncomingServerPassword = v, secret: true)));
        return expander;
    }

    private Gtk.Widget BuildOutgoing()
    {
        var vm = ViewModel;
        var expander = Expander(Translator.ImapSetup_OutgoingTitle, nameof(vm.IsOutgoingExpanded), s => s.IsOutgoingExpanded, (s, v) => s.IsOutgoingExpanded = v);
        Form.Bind(nameof(vm.OutgoingSummary), s => s.OutgoingSummary, text => expander.Subtitle = text ?? string.Empty);
        Form.Visible(expander, nameof(vm.IsMailSupportEnabled), s => s.IsMailSupportEnabled);

        expander.AddRow(GtkUi.FieldRow(Translator.ImapSetup_ServerHeader,
            Form.Text(nameof(vm.OutgoingServer), s => s.OutgoingServer, (s, v) => s.OutgoingServer = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.IMAPSetupDialog_OutgoingMailServerPort,
            Form.Text(nameof(vm.OutgoingServerPort), s => s.OutgoingServerPort, (s, v) => s.OutgoingServerPort = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_ConnectionSecurityHeader,
            Form.DropDown(vm.AvailableConnectionSecurityDisplayNames, nameof(vm.SelectedOutgoingServerConnectionSecurityIndex),
                s => s.SelectedOutgoingServerConnectionSecurityIndex, (s, v) => s.SelectedOutgoingServerConnectionSecurityIndex = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_AuthenticationMethodHeader,
            Form.DropDown(vm.AvailableAuthenticationMethodDisplayNames, nameof(vm.SelectedOutgoingServerAuthenticationMethodIndex),
                s => s.SelectedOutgoingServerAuthenticationMethodIndex, (s, v) => s.SelectedOutgoingServerAuthenticationMethodIndex = v)));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapSetup_UseIncomingSignInForOutgoing,
            Form.Switch(nameof(vm.UseIncomingCredentialsForOutgoing), s => s.UseIncomingCredentialsForOutgoing, (s, v) => s.UseIncomingCredentialsForOutgoing = v)));
        var user = GtkUi.FieldRow(Translator.IMAPSetupDialog_OutgoingMailServerUsername,
            Form.Text(nameof(vm.OutgoingServerUsername), s => s.OutgoingServerUsername, (s, v) => s.OutgoingServerUsername = v));
        var password = GtkUi.FieldRow(Translator.IMAPSetupDialog_OutgoingMailServerPassword,
            Form.Text(nameof(vm.OutgoingServerPassword), s => s.OutgoingServerPassword, (s, v) => s.OutgoingServerPassword = v, secret: true));
        Form.Visible(user, nameof(vm.IsOutgoingCredentialsVisible), s => s.IsOutgoingCredentialsVisible);
        Form.Visible(password, nameof(vm.IsOutgoingCredentialsVisible), s => s.IsOutgoingCredentialsVisible);
        expander.AddRow(user);
        expander.AddRow(password);
        return expander;
    }

    private Gtk.Widget BuildCalDav()
    {
        var vm = ViewModel;
        var expander = Expander(Translator.ImapSetup_CalDavTitle, nameof(vm.IsCalDavExpanded), s => s.IsCalDavExpanded, (s, v) => s.IsCalDavExpanded = v);
        Form.Bind(nameof(vm.CalDavSummary), s => s.CalDavSummary, text => expander.Subtitle = text ?? string.Empty);
        var status = StatusIcon();
        Form.Bind(nameof(vm.CalDavConnectionState), s => s.CalDavConnectionState, state => ShowState(status, state));
        expander.AddSuffix(status);
        Form.Visible(expander, nameof(vm.IsCalDavSettingsVisible), s => s.IsCalDavSettingsVisible);
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_CalDavServiceUrl,
            Form.Text(nameof(vm.CalDavServiceUrl), s => s.CalDavServiceUrl, (s, v) => s.CalDavServiceUrl = v)));
        AddDavCredentials(expander, nameof(vm.IsDavCredentialsOptionVisible), s => s.IsDavCredentialsOptionVisible,
            nameof(vm.IsDavCredentialsVisible), s => s.IsDavCredentialsVisible);
        return expander;
    }

    private Gtk.Widget BuildCardDav()
    {
        var vm = ViewModel;
        var expander = Expander(Translator.ImapSetup_CardDavTitle, nameof(vm.IsCardDavExpanded), s => s.IsCardDavExpanded, (s, v) => s.IsCardDavExpanded = v);
        Form.Bind(nameof(vm.CardDavSummary), s => s.CardDavSummary, text => expander.Subtitle = text ?? string.Empty);
        Form.Visible(expander, nameof(vm.IsCardDavSettingsVisible), s => s.IsCardDavSettingsVisible);
        expander.AddRow(GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_CardDavServiceUrl,
            Form.Text(nameof(vm.CardDavServiceUrl), s => s.CardDavServiceUrl, (s, v) => s.CardDavServiceUrl = v, Translator.ImapCalDavSettingsPage_CardDavDiscoveryPlaceholder),
            Translator.ImapSetup_CardDavSharedSignIn));
        AddDavCredentials(expander, nameof(vm.IsCardDavCredentialsOptionVisible), s => s.IsCardDavCredentialsOptionVisible,
            nameof(vm.IsCardDavCredentialsVisible), s => s.IsCardDavCredentialsVisible);
        return expander;
    }

    /// <summary>"Use the mail sign-in" and, when off, the DAV user name and password.</summary>
    private void AddDavCredentials(Adw.ExpanderRow expander, string optionProperty, Func<ImapCalDavSettingsPageViewModel, bool> option,
        string credentialsProperty, Func<ImapCalDavSettingsPageViewModel, bool> credentials)
    {
        var vm = ViewModel;
        var shared = GtkUi.FieldRow(Translator.ImapSetup_UseMailSignInForDav,
            Form.Switch(nameof(vm.UseMailCredentialsForDav), s => s.UseMailCredentialsForDav, (s, v) => s.UseMailCredentialsForDav = v));
        Form.Visible(shared, optionProperty, option);
        expander.AddRow(shared);
        var user = GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_DavUsername,
            Form.Text(nameof(vm.CalDavUsername), s => s.CalDavUsername, (s, v) => s.CalDavUsername = v));
        var password = GtkUi.FieldRow(Translator.ImapCalDavSettingsPage_DavPassword,
            Form.Text(nameof(vm.CalDavPassword), s => s.CalDavPassword, (s, v) => s.CalDavPassword = v, secret: true));
        Form.Visible(user, credentialsProperty, credentials);
        Form.Visible(password, credentialsProperty, credentials);
        expander.AddRow(user);
        expander.AddRow(password);
    }

    private Gtk.Widget BuildAdvanced()
    {
        var vm = ViewModel;
        var expander = Adw.ExpanderRow.New();
        expander.Title = Translator.ImapSetup_AdvancedTitle;
        expander.Subtitle = Translator.ImapSetup_AdvancedDescription;
        Form.Visible(expander, nameof(vm.IsMailAdvancedOptionsVisible), s => s.IsMailAdvancedOptionsVisible);
        expander.AddRow(GtkUi.FieldRow(Translator.SettingsAccountManagementAppendMessage_Title,
            Form.Switch(nameof(vm.ShouldAppendMessagesToSentFolder), s => s.ShouldAppendMessagesToSentFolder, (s, v) => s.ShouldAppendMessagesToSentFolder = v),
            Translator.SettingsAccountManagementAppendMessage_Description));
        var clients = Gtk.SpinButton.NewWithRange(1, 20, 1);
        clients.Valign = Gtk.Align.Center;
        var clientsBinding = Form.Bind(nameof(vm.MaxConcurrentClients), s => s.MaxConcurrentClients,
            value => { if ((int)clients.Value != value) clients.Value = value; }, (s, v) => s.MaxConcurrentClients = v);
        void ClientsChanged(Gtk.SpinButton sender, EventArgs args) => clientsBinding.UpdateSource((int)sender.Value);
        clients.OnValueChanged += ClientsChanged;
        Bindings.Own(new ActionDisposable(() => clients.OnValueChanged -= ClientsChanged));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapSetup_ConcurrencyTitle, clients, Translator.ImapSetup_ConcurrencyDescription));
        expander.AddRow(GtkUi.FieldRow(Translator.ImapSetup_ProxyTitle,
            GtkUi.Row(6, Form.Text(nameof(vm.ProxyServer), s => s.ProxyServer, (s, v) => s.ProxyServer = v),
                Form.Text(nameof(vm.ProxyServerPort), s => s.ProxyServerPort, (s, v) => s.ProxyServerPort = v)),
            Translator.ImapSetup_ProxyDescription));
        return expander;
    }

    #endregion
}
