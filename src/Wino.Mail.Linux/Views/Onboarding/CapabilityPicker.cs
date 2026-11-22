using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels.Data;
using Wino.Presentation.Bindings;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>
/// What an account is used for (the Windows and Mac AccountCapabilityPicker): Mail, Calendar, Contacts and To Do, each a
/// row with its modes. A capability whose provider service is unavailable offers only this device and Off.
/// </summary>
public static class CapabilityPicker
{
    public static Adw.PreferencesGroup Create(AccountCapabilitySelection selection, BindingScope scope,
        IDispatcher dispatcher, Action<Exception> error, Gtk.Widget? mailOptions = null)
    {
        var form = new GtkForm<AccountCapabilitySelection>(selection, scope, dispatcher, error);
        var group = Adw.PreferencesGroup.New();

        var mail = Adw.ExpanderRow.New();

        mail.UseMarkup = false;
        mail.Title = Translator.ProviderSelection_UseForMail;
        mail.Subtitle = selection.MailDescription;
        mail.ShowEnableSwitch = true;
        form.Bind(nameof(selection.MailMode), s => s.MailMode != AccountCapabilityMode.Off, enabled => { if (mail.EnableExpansion != enabled) mail.EnableExpansion = enabled; },
            (s, enabled) => s.MailMode = enabled ? AccountCapabilityMode.Provider : AccountCapabilityMode.Off);
        void MailChanged(GObject.Object sender, GObject.Object.NotifySignalArgs args)
        {
            if (args.Pspec.GetName() != "enable-expansion") return;
            var mode = mail.EnableExpansion ? AccountCapabilityMode.Provider : AccountCapabilityMode.Off;
            if (selection.MailMode != mode) selection.MailMode = mode;
        }
        mail.OnNotify += MailChanged;
        scope.Own(new ActionDisposable(() => mail.OnNotify -= MailChanged));
        if (mailOptions is not null) mail.AddRow(mailOptions);
        mail.Expanded = true;
        group.Add(mail);

        group.Add(ModeRow(form, Translator.ProviderSelection_UseForCalendar, Translator.ProviderSelection_CalendarStepDescription,
            nameof(selection.CalendarMode), s => s.CalendarMode, (s, m) => s.CalendarMode = m,
            nameof(selection.IsCalendarProviderModeAvailable), s => s.IsCalendarProviderModeAvailable, s => s.CalendarProviderOptionText));
        group.Add(ModeRow(form, Translator.ProviderSelection_UseForContacts, Translator.ProviderSelection_ContactsStepDescription,
            nameof(selection.ContactMode), s => s.ContactMode, (s, m) => s.ContactMode = m,
            nameof(selection.IsContactProviderModeAvailable), s => s.IsContactProviderModeAvailable, s => s.ContactProviderOptionText));
        group.Add(ModeRow(form, Translator.ProviderSelection_UseForTasks, Translator.ProviderSelection_TasksStepDescription,
            nameof(selection.TaskMode), s => s.TaskMode, (s, m) => s.TaskMode = m,
            nameof(selection.IsTaskProviderModeAvailable), s => s.IsTaskProviderModeAvailable, s => s.TaskProviderOptionText));
        return group;
    }

    /// <summary>A row whose drop-down is rebuilt when the provider option appears or goes.</summary>
    private static Adw.ActionRow ModeRow(GtkForm<AccountCapabilitySelection> form, string title, string description,
        string modeProperty, Func<AccountCapabilitySelection, AccountCapabilityMode> read, Action<AccountCapabilitySelection, AccountCapabilityMode> write,
        string availableProperty, Func<AccountCapabilitySelection, bool> available, Func<AccountCapabilitySelection, string> providerText)
    {
        var row = Adw.ActionRow.New();
        row.UseMarkup = false;
        row.Title = title;
        row.Subtitle = description;
        Gtk.DropDown? current = null;
        BindingScope? rowScope = null;
        form.Bind(availableProperty, s => (available(s), providerText(s)), state =>
        {
            if (current is not null) row.Remove(current);
            rowScope?.Dispose();
            rowScope = form.Scope.Own(new BindingScope());
            var modes = state.Item1
                ? new[] { AccountCapabilityMode.Provider, AccountCapabilityMode.Local, AccountCapabilityMode.Off }
                : new[] { AccountCapabilityMode.Local, AccountCapabilityMode.Off };
            var labels = modes.Select(mode => mode switch
            {
                AccountCapabilityMode.Provider => state.Item2,
                AccountCapabilityMode.Local => Translator.CapabilityPicker_LocalOption_Device,
                _ => Translator.ProviderSelection_ModeOff
            });
            var rowForm = new GtkForm<AccountCapabilitySelection>(form.Source, rowScope, form.Dispatcher, form.Error);
            current = rowForm.DropDown(labels, modeProperty, s => Array.IndexOf(modes, read(s)), (s, index) => write(s, modes[index]));
            row.AddSuffix(current);
        });
        return row;
    }
}
