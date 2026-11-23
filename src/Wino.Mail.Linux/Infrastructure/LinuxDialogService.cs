using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Wino.Core.Domain;
using Wino.Core.Domain.Entities.Calendar;
using Wino.Core.Domain.Entities.Mail;
using Wino.Core.Domain.Entities.Shared;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models;
using Wino.Core.Domain.Models.Accounts;
using Wino.Core.Domain.Models.Calendar;
using Wino.Core.Domain.Models.Common;
using Wino.Core.Domain.Models.Connectivity;
using Wino.Core.Domain.Models.Contacts;
using Wino.Core.Domain.Models.Folders;
using Wino.Core.Domain.Models.MailItem;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// Dialogs as libadwaita alert dialogs on the main window, one at a time, and the Windows info bar as toasts.
/// Prompts, lists, text input, certificate trust, IMAP diagnostics and message source work now; forms that belong to
/// screens of later milestones (signatures, aliases, categories, Wino Account) show the not-supported notice and answer
/// as cancelled, as the Android head does for its unbuilt forms.
/// </summary>
public sealed class LinuxDialogService(IDispatcher dispatcher, ShellWindow window, IServiceProvider services) : IMailDialogService
{
    private const string Accept = "accept";
    private const string Secondary = "secondary";
    private const string Cancel = "cancel";
    private readonly SemaphoreSlim _presentations = new(1, 1);

    #region Presentation

    /// <summary>
    /// One alert at a time on the main window. Answers the chosen response id, or <see cref="Cancel"/> when closed.
    /// <paramref name="configure"/> adds responses and content on the UI thread before the alert is shown.
    /// </summary>
    private async Task<string> AlertAsync(string? heading, string? body, Action<Adw.AlertDialog, TaskCompletionSource<string>> configure)
    {
        await _presentations.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await dispatcher.ExecuteOnUIThread(() =>
            {
                var alert = Adw.AlertDialog.New(heading, body);
                configure(alert, completion);
                alert.OnResponse += (_, args) => completion.TrySetResult(args.Response);
                alert.OnClosed += (_, _) => completion.TrySetResult(Cancel);
                alert.Present(window.Widget);
            });
            return await completion.Task;
        }
        finally { _presentations.Release(); }
    }

    /// <summary>A prompt with an accept button, an optional secondary button and an optional cancel button.</summary>
    private Task<string> PromptAsync(string? heading, string? body, string accept, string? cancel = null, string? secondary = null,
        bool destructive = false, Gtk.Widget? extra = null)
        => AlertAsync(heading, body, (alert, _) =>
        {
            if (!string.IsNullOrEmpty(cancel)) alert.AddResponse(Cancel, cancel);
            if (!string.IsNullOrEmpty(secondary)) alert.AddResponse(Secondary, secondary);
            alert.AddResponse(Accept, accept);
            alert.SetResponseAppearance(Accept, destructive ? Adw.ResponseAppearance.Destructive : Adw.ResponseAppearance.Suggested);
            alert.DefaultResponse = Accept;
            alert.CloseResponse = Cancel;
            if (extra is not null) alert.ExtraChild = extra;
        });

    /// <summary>A single-choice list; null when cancelled. Items that cannot be chosen say why in a toast.</summary>
    private async Task<T?> PickAsync<T>(string title, IReadOnlyList<(T Item, string Text, string? Invalid)> items) where T : class
    {
        T? picked = null;
        await AlertAsync(title, null, (alert, completion) =>
        {
            var list = Gtk.ListBox.New();
            list.AddCssClass("boxed-list");
            list.SelectionMode = Gtk.SelectionMode.None;
            foreach (var (_, text, invalid) in items)
            {
                var row = Adw.ActionRow.New();
                row.UseMarkup = false;
                row.Title = text;
                if (invalid is not null) row.Subtitle = invalid;
                row.Activatable = true;
                list.Append(row);
            }
            list.OnRowActivated += (_, args) =>
            {
                var index = args.Row.GetIndex();
                if (index < 0 || index >= items.Count) return;
                var choice = items[index];
                if (choice.Invalid is not null)
                {
                    InfoBarMessage(choice.Text, choice.Invalid, InfoBarMessageType.Warning);
                    return;
                }
                picked = choice.Item;
                completion.TrySetResult(Accept);
                alert.ForceClose();
            };
            var scroller = Gtk.ScrolledWindow.New();
            scroller.Child = list;
            scroller.PropagateNaturalHeight = true;
            scroller.MaxContentHeight = 420;
            scroller.HscrollbarPolicy = Gtk.PolicyType.Never;
            alert.ExtraChild = scroller;
            alert.AddResponse(Cancel, Translator.Buttons_Cancel);
            alert.CloseResponse = Cancel;
        });
        return picked;
    }

    private static Gtk.Label TextBlock(string text, bool selectable = false, string? cssClass = null)
    {
        var label = Gtk.Label.New(text);
        label.Wrap = true;
        label.Xalign = 0;
        label.Selectable = selectable;
        if (cssClass is not null) label.AddCssClass(cssClass);
        return label;
    }

    private static Gtk.Box Column() => Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    #endregion

    #region Prompts

    public async Task<bool> ShowConfirmationDialogAsync(string question, string title, string confirmationButtonTitle)
        => await PromptAsync(title, question, confirmationButtonTitle, Translator.Buttons_Cancel) == Accept;

    public async Task ShowMessageAsync(string message, string title, WinoCustomMessageDialogIcon icon)
        => await PromptAsync(title, message, Translator.Buttons_OK);

    public async Task<ThreeButtonDialogResult> ShowThreeButtonDialogAsync(string title, string description, string primaryButtonText, string secondaryButtonText, string cancelButtonText, WinoCustomMessageDialogIcon? icon = null)
        => await PromptAsync(title, description, primaryButtonText, cancelButtonText, secondaryButtonText) switch
        {
            Accept => ThreeButtonDialogResult.Primary,
            Secondary => ThreeButtonDialogResult.Secondary,
            _ => ThreeButtonDialogResult.Cancel
        };

    public async Task<bool> ShowOpenLinkConfirmationAsync(string? linkText, Uri originalUri, Uri cleanedUri, int removedParameterCount, string? browserName)
        => await PromptAsync(Translator.OpenLinkDialog_Title,
            Wino.Core.Domain.Models.Reader.OpenLinkConfirmationText.GetMessage(linkText, cleanedUri, removedParameterCount, browserName),
            Translator.OpenLinkDialog_OpenLink, Translator.Buttons_Cancel) == Accept;

    /// <summary>Same contract as Windows: a key already marked "don't ask again" answers false without showing.</summary>
    public async Task<bool> ShowWinoCustomMessageDialogAsync(string title, string description, string approveButtonText, WinoCustomMessageDialogIcon? icon, string cancelButtonText = "", string dontAskAgainConfigurationKey = "")
    {
        var configuration = services.GetService<IConfigurationService>();
        var dontAskKey = string.IsNullOrEmpty(dontAskAgainConfigurationKey) || configuration is null ? null : dontAskAgainConfigurationKey;
        if (dontAskKey is not null && configuration is not null && configuration.Get(dontAskKey, false)) return false;

        Gtk.CheckButton? dontAsk = null;
        var response = await AlertAsync(title, description, (alert, _) =>
        {
            if (!string.IsNullOrEmpty(cancelButtonText)) alert.AddResponse(Cancel, cancelButtonText);
            alert.AddResponse(Accept, approveButtonText);
            alert.SetResponseAppearance(Accept, icon == WinoCustomMessageDialogIcon.Warning ? Adw.ResponseAppearance.Destructive : Adw.ResponseAppearance.Suggested);
            alert.DefaultResponse = Accept;
            alert.CloseResponse = Cancel;
            if (dontAskKey is not null)
            {
                dontAsk = Gtk.CheckButton.NewWithLabel(Translator.Dialog_DontAskAgain);
                alert.ExtraChild = dontAsk;
            }
        });
        if (dontAskKey is not null && configuration is not null && dontAsk is { Active: true }) configuration.Set(dontAskKey, true);
        return response == Accept;
    }

    public Task<bool> ShowHardDeleteConfirmationAsync()
        => ShowWinoCustomMessageDialogAsync(Translator.DialogMessage_HardDeleteConfirmationMessage, Translator.DialogMessage_HardDeleteConfirmationTitle,
            Translator.Buttons_Yes, WinoCustomMessageDialogIcon.Warning, Translator.Buttons_No);

    /// <summary>An empty string when cancelled (callers test for empty input).</summary>
    public async Task<string> ShowTextInputDialogAsync(string currentInput, string dialogTitle, string dialogDescription, string primaryButtonText)
    {
        Gtk.Entry? entry = null;
        var input = string.Empty;
        var response = await AlertAsync(dialogTitle, dialogDescription, (alert, _) =>
        {
            entry = Gtk.Entry.New();
            entry.SetText(currentInput ?? string.Empty);
            entry.ActivatesDefault = true;
            alert.ExtraChild = entry;
            alert.AddResponse(Cancel, Translator.Buttons_Cancel);
            alert.AddResponse(Accept, primaryButtonText);
            alert.SetResponseAppearance(Accept, Adw.ResponseAppearance.Suggested);
            alert.DefaultResponse = Accept;
            alert.CloseResponse = Cancel;
            alert.OnResponse += (_, _) => input = entry.GetText();
        });
        return response == Accept ? input : string.Empty;
    }

    public Task<bool> ShowWinoAccountExportDialogAsync() => ShowConfirmationDialogAsync(
        $"{Translator.WinoAccount_Management_ExportDialog_Confirmation}\n\n{Translator.WinoAccount_Management_ExportDialog_EncryptionNotice}\n\n{Translator.WinoAccount_Management_ExportDialog_AccountsDisclaimer} {Translator.WinoAccount_Management_ExportDialog_AccountsRelogin_Device}",
        Translator.WinoAccount_Management_ExportDialog_Title, Translator.WinoAccount_Management_LocalDataExportAction);

    /// <summary>The error, the end of the protocol log, Close and Copy diagnostics (Windows ImapValidationFailedDialog).</summary>
    public async Task ShowImapValidationFailedDialogAsync(string errorMessage, string protocolLog)
    {
        var log = protocolLog ?? string.Empty;
        var shown = log.Length > 1500 ? "…" + log[^1500..] : log;
        var message = string.IsNullOrWhiteSpace(shown) ? errorMessage : $"{errorMessage}\n\n{Translator.ImapValidationFailedDialog_ProtocolLog}\n{shown}";
        var response = await PromptAsync(Translator.ImapValidationFailedDialog_Title, message, Translator.Buttons_Close, secondary: Translator.ImapValidationFailedDialog_Copy);
        if (response == Secondary) await CopyAsync($"{errorMessage}{Environment.NewLine}{Environment.NewLine}{log}");
    }

    /// <summary>
    /// Fails closed: Cancel or closing answers false. A dangerous prompt keeps the accept response disabled until its
    /// acknowledgement is ticked.
    /// </summary>
    public async Task<bool> ShowServerCertificateTrustDialogAsync(ServerCertificateTrustPrompt prompt)
    {
        Gtk.CheckButton? acknowledgement = null;
        var response = await AlertAsync(prompt.Title, null, (alert, _) =>
        {
            var column = Column();
            column.Append(TextBlock(prompt.Intro, cssClass: prompt.IsDangerous ? "error" : null));
            if (prompt.Problems.Length > 0)
                column.Append(TextBlock($"{prompt.ProblemsHeader}\n" + string.Join("\n", prompt.Problems.Select(problem => "• " + problem))));
            if (prompt.Facts.Length > 0)
                column.Append(TextBlock(string.Join("\n", prompt.Facts.Select(fact => $"{fact.Label}: {fact.Value}")), selectable: true, cssClass: "dim-label"));
            column.Append(TextBlock(prompt.Closing));

            alert.AddResponse(Cancel, Translator.Buttons_Cancel);
            alert.AddResponse(Accept, prompt.AcceptButtonText);
            alert.SetResponseAppearance(Accept, prompt.IsDangerous ? Adw.ResponseAppearance.Destructive : Adw.ResponseAppearance.Suggested);
            alert.DefaultResponse = Cancel;
            alert.CloseResponse = Cancel;
            if (prompt.IsDangerous)
            {
                var check = Gtk.CheckButton.NewWithLabel(prompt.AcknowledgementText);
                acknowledgement = check;
                alert.SetResponseEnabled(Accept, false);
                check.OnToggled += (_, _) => alert.SetResponseEnabled(Accept, check.Active);
                column.Append(check);
            }

            var scroller = Gtk.ScrolledWindow.New();
            scroller.Child = column;
            scroller.PropagateNaturalHeight = true;
            scroller.MaxContentHeight = 480;
            scroller.HscrollbarPolicy = Gtk.PolicyType.Never;
            alert.ExtraChild = scroller;
        });
        return response == Accept && (acknowledgement is null || acknowledgement.Active);
    }

    public async Task ShowMessageSourceDialogAsync(string messageSource)
    {
        var source = TextBlock(messageSource ?? string.Empty, selectable: true, cssClass: "monospace");
        var scroller = Gtk.ScrolledWindow.New();
        scroller.Child = source;
        scroller.SetSizeRequest(560, 360);
        var response = await PromptAsync(Translator.MessageSourceDialog_Title, null, Translator.Buttons_Close, secondary: Translator.Buttons_Copy, extra: scroller);
        if (response != Secondary) return;
        await CopyAsync(messageSource ?? string.Empty);
        InfoBarMessage(Translator.MessageSourceDialog_Title, Translator.MessageSourceDialog_Copied, InfoBarMessageType.Success);
    }

    private Task CopyAsync(string text)
        => services.GetService<IClipboardService>()?.CopyTextAsync(text) ?? Task.CompletedTask;

    #endregion

    #region Info bar

    public void InfoBarMessage(string title, string message, InfoBarMessageType messageType) => ShowToast(title, message, null, null);

    public void InfoBarMessage(string title, string message, InfoBarMessageType messageType, string actionButtonText, Action action)
        => ShowToast(title, message, actionButtonText, action);

    public void ShowNotSupportedMessage()
        => ShowToast(Translator.Info_UnsupportedFunctionalityTitle, Translator.Info_UnsupportedFunctionalityDescription, null, null);

    public void ShowReadOnlyCalendarMessage()
        => ShowToast(Translator.CalendarReadOnly_Title, Translator.CalendarReadOnly_Message, null, null);

    /// <summary>The Windows ShellInfoBar as a toast: title and message, an optional action, a longer time with an action.</summary>
    private async void ShowToast(string title, string message, string? actionTitle, Action? action)
    {
        try
        {
            await dispatcher.ExecuteOnUIThread(() =>
            {
                var text = string.IsNullOrWhiteSpace(title) ? message : string.IsNullOrWhiteSpace(message) ? title : $"{title}: {message}";
                var toast = Adw.Toast.New(text);
                toast.Timeout = actionTitle is null ? 5u : 8u;
                if (actionTitle is not null && action is not null)
                {
                    toast.ButtonLabel = actionTitle;
                    toast.OnButtonClicked += (_, _) => action();
                }
                window.ShowToast(toast);
            });
        }
        catch (Exception exception) { Serilog.Log.Warning(exception, "Could not show an info bar message."); }
    }

    #endregion

    #region Pickers

    public async Task<IMailItemFolder?> ShowMoveMailFolderDialogAsync(List<IMailItemFolder> availableFolders)
    {
        var rows = new List<(IMailItemFolder, string, string?)>();
        void Add(IEnumerable<IMailItemFolder> folders, int depth)
        {
            foreach (var folder in folders)
            {
                var name = folder.ResolvedDisplayName ?? string.Empty;
                rows.Add((folder, new string(' ', depth * 4) + name,
                    folder.IsMoveTarget ? null : string.Format(Translator.MoveMailDialog_InvalidFolderMessage, name)));
                Add(folder.ChildFolders ?? [], depth + 1);
            }
        }
        Add(availableFolders ?? [], 0);
        return await PickAsync(Translator.MoveMailDialog_Title, rows);
    }

    public async Task<IMailItemFolder?> PickFolderAsync(Guid accountId, PickFolderReason reason, IFolderService folderService)
    {
        var structure = await folderService.GetFolderStructureForAccountAsync(accountId, false);
        return await ShowMoveMailFolderDialogAsync(structure.Folders);
    }

    public Task<MailAccount?> ShowAccountPickerDialogAsync(List<MailAccount> availableAccounts)
        => PickAsync(Translator.AccountPickerDialog_Title, (availableAccounts ?? []).Select(account => (account, AccountLine(account), (string?)null)).ToList());

    /// <summary>The account for a new mail from several accounts; Cancel or closing answer null.</summary>
    public Task<MailAccount?> PickAccountAsync(IReadOnlyList<MailAccount> accounts, string title)
        => PickAsync(string.IsNullOrWhiteSpace(title) ? Translator.AccountPickerDialog_Title : title,
            accounts.Select(account => (account, AccountChoiceLine(account), (string?)null)).ToList());

    public async Task<AccountCalendarPickingResult> ShowSingleCalendarPickerDialogAsync(List<CalendarPickerAccountGroup> availableCalendarGroups)
    {
        var rows = (availableCalendarGroups ?? []).SelectMany(group => (group.Calendars ?? []).Select(calendar =>
            (calendar, $"{calendar.Name} — {AccountLine(group.Account)}", calendar.IsReadOnly ? Translator.CalendarPicker_ReadOnly : (string?)null))).ToList();
        var picked = await PickAsync(Translator.CalendarEventCompose_PickCalendarTitle, rows);
        return new AccountCalendarPickingResult(picked, false);
    }

    public Task<ContactCreateDestination?> ShowContactDestinationPickerDialogAsync(IReadOnlyList<ContactCreateDestination> destinations)
        => PickAsync(Translator.PeopleSettings_DestinationPicker_Title, (destinations ?? []).Select(destination =>
            (destination, destination.DisplayName, destination.IsReadOnly ? Translator.CalendarPicker_ReadOnly : (string?)null)).ToList());

    public Task<AccountTaskList?> ShowTaskListPickerDialogAsync(IReadOnlyList<AccountTaskList> taskLists, IReadOnlyList<MailAccount> accounts)
    {
        var owners = (accounts ?? []).ToDictionary(account => account.Id);
        return PickAsync(Translator.ToDoPage_Title, (taskLists ?? []).Select(list =>
            (list, owners.TryGetValue(list.MailAccountId, out var owner) && !string.IsNullOrWhiteSpace(owner.Name) ? $"{list.Title} — {owner.Name}" : list.Title ?? string.Empty,
             (string?)null)).ToList());
    }

    /// <summary>The list name, then its account from a picker (Windows NewContactListDialog).</summary>
    public async Task<ContactListCreationResult?> ShowNewContactListDialogAsync(IReadOnlyList<MailAccount> accounts, MailAccount? selectedAccount)
    {
        var name = (await ShowTextInputDialogAsync(string.Empty, Translator.ContactList_NewTitle, Translator.ContactList_NameHeader, Translator.Buttons_Create)).Trim();
        if (name.Length == 0) return null;
        var available = (accounts ?? []).ToList();
        var owner = available.Count == 1 ? available[0] : selectedAccount ?? await ShowAccountPickerDialogAsync(available);
        return owner is null ? null : new ContactListCreationResult(name, owner);
    }

    /// <summary>"Name (address)", or whichever of the two exists.</summary>
    private static string AccountChoiceLine(MailAccount account)
    {
        var name = account.Name?.Trim() ?? string.Empty;
        var address = account.Address?.Trim() ?? string.Empty;
        if (name.Length == 0) return address;
        if (address.Length == 0 || string.Equals(name, address, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} ({address})";
    }

    /// <summary>"Name · address", or whichever of the two exists.</summary>
    private static string AccountLine(MailAccount? account)
    {
        var name = account?.Name?.Trim() ?? string.Empty;
        var address = account?.Address?.Trim() ?? string.Empty;
        if (name.Length == 0) return address;
        if (address.Length == 0 || string.Equals(name, address, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} · {address}";
    }

    #endregion

    #region Account setup

    public IAccountCreationDialog GetAccountCreationDialog(AccountCreationDialogResult accountCreationDialogResult) => new AccountProgress(dispatcher, window);

    /// <summary>Not called by shared code: account creation goes through the onboarding provider page.</summary>
    public Task<AccountCreationDialogResult> ShowAccountProviderSelectionDialogAsync(List<IProviderDetail> availableProviders)
        => Task.FromException<AccountCreationDialogResult>(new NotSupportedException("Linux adds accounts through the provider selection page."));

    /// <summary>The "adding account" dialog while sign-in and setup run. It never holds the presentation gate.</summary>
    private sealed class AccountProgress(IDispatcher dispatcher, ShellWindow window) : IAccountCreationDialog
    {
        private Adw.AlertDialog? _dialog;

        public AccountCreationDialogState State { get; set; }

        public Task ShowDialogAsync(CancellationTokenSource cancellationTokenSource) => dispatcher.ExecuteOnUIThread(() =>
        {
            var dialog = Adw.AlertDialog.New(Translator.MacOS_AccountCreation_Title, Translator.MacOS_AccountCreation_BrowserMessage);
            dialog.ExtraChild = Adw.Spinner.New();
            dialog.AddResponse(Cancel, Translator.Buttons_Cancel);
            dialog.CloseResponse = Cancel;
            dialog.OnResponse += (_, _) => cancellationTokenSource.Cancel();
            _dialog = dialog;
            dialog.Present(window.Widget);
        });

        public async void Complete(bool cancel)
        {
            try
            {
                await dispatcher.ExecuteOnUIThread(() =>
                {
                    _dialog?.ForceClose();
                    _dialog = null;
                });
            }
            catch (Exception exception) { Serilog.Log.Warning(exception, "Could not close the account progress dialog."); }
        }
    }

    #endregion

    #region Files

    /// <summary>
    /// The GTK folder chooser (through the file chooser portal where one runs) on the main window. Answers the folder's
    /// local path, or an empty string when cancelled or when the chosen folder has no local path.
    /// </summary>
    public async Task<string> PickWindowsFolderAsync()
    {
        await _presentations.WaitAsync();
        try
        {
            Task<Gio.File?>? picking = null;
            await dispatcher.ExecuteOnUIThread(() =>
            {
                var chooser = Gtk.FileDialog.New();
                chooser.Modal = true;
                picking = chooser.SelectFolderAsync(window.Widget as Gtk.Window);
            });
            try { return (picking is null ? null : await picking)?.GetPath() ?? string.Empty; }
            catch (GLib.GException) { return string.Empty; }
        }
        finally { _presentations.Release(); }
    }

    #endregion

    #region Not built for Linux yet

    private T NotYet<T>(T cancelled, string feature)
    {
        Serilog.Log.Information("{Feature} is not available on Linux yet.", feature);
        ShowNotSupportedMessage();
        return cancelled;
    }

    public Task<List<SharedFile>> PickFilesAsync(params object[] typeFilters) => Task.FromResult(NotYet(new List<SharedFile>(), "File picking"));
    public Task<byte[]> PickWindowsFileContentAsync(params object[] typeFilters) => Task.FromResult(NotYet(Array.Empty<byte>(), "File picking"));
    public Task<List<PickedFileMetadata>> PickFilesMetadataAsync(params object[] typeFilters) => Task.FromResult(NotYet(new List<PickedFileMetadata>(), "File picking"));
    public Task<string> PickFilePathAsync(string saveFileName) => Task.FromResult(NotYet(string.Empty, "Saving files"));

    public Task HandleSystemFolderConfigurationDialogAsync(Guid accountId, IFolderService folderService) => Task.FromResult(NotYet(0, "System folder configuration"));
    public Task ShowAccountReorderDialogAsync(ObservableCollection<IAccountProviderDetailViewModel> availableAccounts) => Task.FromResult(NotYet(0, "Account reordering"));
    public Task<AccountSignature?> ShowSignatureEditorDialog(AccountSignature? signatureModel = null) => Task.FromResult(NotYet<AccountSignature?>(null, "The signature editor"));
    public Task<ICreateAccountAliasDialog> ShowCreateAccountAliasDialogAsync() => Task.FromResult<ICreateAccountAliasDialog>(NotYet(new CancelledAliasDialog(), "Account aliases"));
    public Task<MailCategoryDialogResult?> ShowEditMailCategoryDialogAsync(MailCategory? category = null) => Task.FromResult(NotYet<MailCategoryDialogResult?>(null, "Mail categories"));
    public Task<FolderRenameDialogResult?> ShowRenameFolderDialogAsync(string folderName, string? displayName, bool canRenameOnServer) => Task.FromResult(NotYet<FolderRenameDialogResult?>(null, "Renaming folders"));
    public Task<KeyboardShortcutDialogResult> ShowKeyboardShortcutDialogAsync(KeyboardShortcut? existingShortcut = null) => Task.FromResult(NotYet(new KeyboardShortcutDialogResult(), "Keyboard shortcuts"));

    // Wino Account stays hidden in WinoLux (L4 in the Linux design decisions).
    public Task<WinoAccount?> ShowWinoAccountRegistrationDialogAsync() => Task.FromResult(NotYet<WinoAccount?>(null, "Wino Account"));
    public Task<WinoAccount?> ShowWinoAccountLoginDialogAsync() => Task.FromResult(NotYet<WinoAccount?>(null, "Wino Account"));
    public Task<string?> ShowWinoAccountSyncSecretDialogAsync(SyncSnapshotSecretRequest request) => Task.FromResult(NotYet<string?>(null, "Wino Account"));
    public Task<UnlimitedAccountsPurchaseChannel?> ShowUnlimitedAccountsPurchaseChannelDialogAsync() => Task.FromResult(NotYet<UnlimitedAccountsPurchaseChannel?>(null, "Purchases"));

    private sealed class CancelledAliasDialog : ICreateAccountAliasDialog
    {
        public MailAccountAlias CreatedAccountAlias { get; set; } = null!;
    }

    #endregion
}
