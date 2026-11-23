using System.Collections.Specialized;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Editor;
using Wino.Editor.Gtk;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;
using Wino.Mail.ViewModels.Collections;
using Wino.Mail.ViewModels.Data;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The reading pane's message view over the shared <see cref="MailRenderingPageViewModel"/> (Windows MailRenderingPage,
/// Mac MailRenderingPageViewController): the subject; the sender block (avatar, name and address, To, Cc and Bcc,
/// the date); the remote-content bars with Load once; the body in a card through the shared reader engine in a
/// WebKitGTK view (<see cref="WebKitGtkHtmlMailReaderSession"/>); the attachment strip with Save all; and the loading
/// spinner. The page is reused across selections (<see cref="RenavigateAsync"/>). The command bar arrives with the mail
/// actions (milestone 2f).
/// </summary>
public sealed class MailRenderingPage(MailRenderingPageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger,
    LinuxSenderPictureProvider pictures) : LinuxPage<MailRenderingPageViewModel>(viewModel, dispatcher, logger)
{
    private readonly Gtk.Label _subject = GtkUi.Label(string.Empty, "title-3");
    private readonly Adw.Avatar _avatar = Adw.Avatar.New(40, null, true);
    private readonly Gtk.Label _senderName = GtkUi.Label(string.Empty, "heading");
    private readonly Gtk.Label _senderAddress = GtkUi.Label(string.Empty, "dim-label");
    private readonly Gtk.Label _recipients = GtkUi.Label(string.Empty, "dim-label", "caption");
    private readonly Gtk.Label _date = GtkUi.Label(string.Empty, "dim-label", "caption");
    private readonly InfoBar _withheldBar = new(InfoBarSeverity.Informational, Translator.Reader_Privacy_WithheldTitle);
    private readonly InfoBar _junkBar = new(InfoBarSeverity.Warning, Translator.Reader_Privacy_JunkBarTitle, Translator.Reader_Privacy_JunkBarMessage);
    private readonly InfoBar _ruleBar = new(InfoBarSeverity.Error);
    private readonly Gtk.Box _privacyBars = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
    private readonly Gtk.Overlay _bodyOverlay = Gtk.Overlay.New();
    private readonly Gtk.Label _linkTarget = GtkUi.Label(string.Empty, "wino-link-target");
    private readonly Adw.Spinner _loading = Adw.Spinner.New();
    private readonly Gtk.FlowBox _attachmentTiles = Gtk.FlowBox.New();
    private readonly Gtk.Button _saveAll = Gtk.Button.New();
    private readonly Gtk.Box _attachmentStrip = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
    private WebKit.WebView? _webView;
    private WebKitGtkHtmlMailReaderSession? _reader;
    private MailItemViewModel? _currentItem;
    private string _currentRenderedHtml = string.Empty;
    private bool _dark;
    private bool _disposedReader;
    private bool _attachmentsQueued;

    protected override Gtk.Widget BuildView()
    {
        _subject.Wrap = true;
        _subject.Selectable = true;
        _subject.MarginTop = 14;

        // ---- Sender block: avatar | name, address and recipients | date ----
        _avatar.Valign = Gtk.Align.Start;
        _senderName.Selectable = true;
        _senderAddress.Selectable = true;
        _senderAddress.Ellipsize = Pango.EllipsizeMode.End;
        _recipients.Wrap = true;
        _recipients.Selectable = true;
        var nameLine = GtkUi.Row(6, _senderName, _senderAddress);
        var senderText = GtkUi.Column(3, nameLine, _recipients);
        senderText.Hexpand = true;
        _date.Valign = Gtk.Align.Start;
        _date.Xalign = 1;
        var senderRow = GtkUi.Row(12, _avatar, senderText, _date);

        // ---- Remote content bars (not closable, as on Windows) ----
        foreach (var bar in new[] { _withheldBar, _junkBar, _ruleBar })
        {
            bar.SetAction(Translator.Reader_Privacy_LoadOnce, () => Observe(ViewModel.LoadOnceCommand.ExecuteAsync(null)));
            bar.Widget.Visible = false;
            _privacyBars.Append(bar.Widget);
        }
        _privacyBars.Visible = false;

        var header = GtkUi.Column(12, _subject, senderRow, _privacyBars);
        header.MarginStart = header.MarginEnd = 24;
        header.MarginBottom = 10;

        // ---- Body card ----
        var card = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        card.AddCssClass("card");
        card.AddCssClass("wino-reader-card");
        card.Overflow = Gtk.Overflow.Hidden;
        card.Vexpand = true;
        card.MarginStart = card.MarginEnd = 24;
        card.MarginTop = 6;
        card.MarginBottom = 16;
        _bodyOverlay.Vexpand = true;
        if (WebKitGtkRuntime.TryInitialize(out var error) && CreateWebView() is { } webView)
        {
            _webView = webView;
            _bodyOverlay.Child = webView;
        }
        else
        {
            if (error is not null) Serilog.Log.Warning(error, "WebKitGTK could not be loaded.");
            var missing = Adw.StatusPage.New();
            missing.IconName = "dialog-warning-symbolic";
            missing.Description = GLib.Functions.MarkupEscapeText(string.Format(Translator.Linux_Reader_WebKitMissing, WebKitGtkRuntime.PackageName), -1);
            missing.AddCssClass("compact");
            _bodyOverlay.Child = missing;
        }
        _linkTarget.Halign = Gtk.Align.Start;
        _linkTarget.Valign = Gtk.Align.End;
        _linkTarget.Ellipsize = Pango.EllipsizeMode.Middle;
        _linkTarget.MaxWidthChars = 80;
        _linkTarget.Visible = false;
        _bodyOverlay.AddOverlay(_linkTarget);
        _loading.SetSizeRequest(32, 32);
        _loading.Halign = _loading.Valign = Gtk.Align.Center;
        _loading.Visible = false;
        _bodyOverlay.AddOverlay(_loading);
        card.Append(_bodyOverlay);

        // ---- Attachments ----
        _attachmentTiles.SelectionMode = Gtk.SelectionMode.None;
        _attachmentTiles.ColumnSpacing = _attachmentTiles.RowSpacing = 8;
        _attachmentTiles.MaxChildrenPerLine = 6;
        _attachmentTiles.Hexpand = true;
        _saveAll.Child = GtkUi.Row(6, WinoIcons.Label(WinoIconGlyph.ArrowDownload, 14), GtkUi.Label(Translator.Reader_SaveAllAttachmentButtonText));
        _saveAll.Valign = Gtk.Align.Start;
        _saveAll.OnClicked += (_, _) => Observe(ViewModel.SaveAllAttachmentsCommand.ExecuteAsync(null));
        _attachmentStrip.Append(_attachmentTiles);
        _attachmentStrip.Append(_saveAll);
        _attachmentStrip.MarginStart = _attachmentStrip.MarginEnd = 24;
        _attachmentStrip.MarginBottom = 16;
        _attachmentStrip.Visible = false;

        var root = GtkUi.Column(0, header, card, _attachmentStrip);
        root.Vexpand = true;
        return root;
    }

    /// <summary>The message view. Null when WebKitGTK is installed but cannot create a view (the error is logged).</summary>
    private WebKit.WebView? CreateWebView()
    {
        try
        {
            var view = WebKit.WebView.New();
            view.Vexpand = true;
            view.Hexpand = true;
            view.TooltipText = null;
            return view;
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "The message view could not be created.");
            return null;
        }
    }

    protected override async Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        _currentItem = parameter as MailItemViewModel;
        var styles = Adw.StyleManager.GetDefault();
        _dark = styles.Dark;
        ViewModel.IsDarkWebviewRenderer = _dark;
        if (_webView is not null)
        {
            _reader = new WebKitGtkHtmlMailReaderSession(_webView, Dispatcher);
            _reader.Configure(ViewModel.ExternalLauncher);
            _reader.OperationFailed += (_, exception) => ReportError(exception);
            _reader.LinkHovered += (_, args) => OnUI(() => ShowLinkTarget(args.Uri));
            _reader.NavigationRequested += (_, _) => OnUI(() => ShowLinkTarget(null));
            // Links go through the ViewModel: tracking parameters are stripped and the link is confirmed when asked.
            _reader.LinkOpener = (uri, text) => ViewModel.OpenLinkAsync(uri, text);
        }
        ViewModel.RenderHtmlAsyncFunc = RenderAsync;
        ViewModel.ClearRenderedHtmlAsyncFunc = ClearAsync;
        ViewModel.CloseRequested += CloseRequested;

        void DarkChanged(GObject.Object sender, GObject.Object.NotifySignalArgs args) => ApplyTheme(styles.Dark);
        styles.OnNotify += DarkChanged;
        Bindings.Own(new ActionDisposable(() => styles.OnNotify -= DarkChanged));

        Form.Bind(nameof(ViewModel.Subject), vm => vm.Subject, subject =>
            _subject.SetText(string.IsNullOrWhiteSpace(subject) ? Translator.MailItemNoSubject : subject));
        Form.Bind(nameof(ViewModel.FromName), vm => vm.FromName, _ => UpdateSender());
        Form.Bind(nameof(ViewModel.FromAddress), vm => vm.FromAddress, _ => UpdateSender());
        EventHandler<string?> pictureChanged = (_, address) =>
        {
            if (address is null || string.Equals(ViewModel.FromAddress?.Trim(), address, StringComparison.OrdinalIgnoreCase)) OnUI(UpdateSender);
        };
        pictures.PictureChanged += pictureChanged;
        Bindings.Own(new ActionDisposable(() => pictures.PictureChanged -= pictureChanged));
        Form.Bind(nameof(ViewModel.CreationDate), vm => vm.CreationDate, _ => UpdateRecipients());
        Form.Bind(nameof(ViewModel.IsMailContentReady), vm => vm.IsMailContentReady, ready =>
        {
            _loading.Visible = !ready;
            _subject.Opacity = ready ? 1 : 0;
        });
        Form.Bind(nameof(ViewModel.CurrentRenderModel), vm => vm.CurrentRenderModel, _ =>
        {
            UpdatePrivacy();
            UpdateRecipients();
            UpdateAttachments();
        });
        foreach (var property in new[]
        {
            nameof(ViewModel.IsRemoteContentWithheld), nameof(ViewModel.IsJunkBlocked), nameof(ViewModel.IsSenderBlockedByRule),
            nameof(ViewModel.WithheldBarMessage), nameof(ViewModel.RuleBarTitle), nameof(ViewModel.RuleBarMessage),
            nameof(ViewModel.IsRemoteContentBarVisible)
        })
            Form.Bind(property, _ => 0, _ => UpdatePrivacy());
        ObserveCollection(ViewModel.ToItems, attachments: false);
        ObserveCollection(ViewModel.CcItems, attachments: false);
        ObserveCollection(ViewModel.BccItems, attachments: false);
        ObserveCollection(ViewModel.Attachments, attachments: true);
        RegisterReaderDebugCommands();

        if (_reader is not null)
        {
            await _reader.InitializeAsync();
            await _reader.SetThemeAsync(_dark);
        }
        await ViewModel.InitializeNavigationAsync(mode, parameter!);
    }

    /// <summary>Loads another message into this reader without recreating the web view.</summary>
    public async Task RenavigateAsync(object? parameter)
    {
        await Dispatcher.ExecuteOnUIThread(() =>
        {
            _currentItem = parameter as MailItemViewModel;
            ShowLinkTarget(null);
        });
        await ViewModel.InitializeNavigationAsync(NavigationMode.New, parameter!);
    }

    protected override async Task DeactivateAsync()
    {
        ViewModel.CloseRequested -= CloseRequested;
        ViewModel.RenderHtmlAsyncFunc = null;
        ViewModel.ClearRenderedHtmlAsyncFunc = null;
        ViewModel.OnNavigatedFrom(NavigationMode.New, null!);
        await DisposeReaderAsync();
    }

    private void ObserveCollection(INotifyCollectionChanged collection, bool attachments)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => OnUI(() =>
        {
            if (!attachments) { UpdateRecipients(); return; }
            // The attachment collection changes item by item while a message loads; one rebuild per main-loop turn.
            if (_attachmentsQueued) return;
            _attachmentsQueued = true;
            GLib.Functions.IdleAdd(200, () =>
            {
                _attachmentsQueued = false;
                if (!Bindings.IsDisposed) UpdateAttachments();
                return false;
            });
        });
        collection.CollectionChanged += handler;
        Bindings.Own(new ActionDisposable(() => collection.CollectionChanged -= handler));
    }

    private void UpdateSender()
    {
        var name = string.IsNullOrWhiteSpace(ViewModel.FromName) ? ViewModel.FromAddress : ViewModel.FromName;
        _senderName.SetText(name ?? string.Empty);
        _senderAddress.SetText(string.Equals(name, ViewModel.FromAddress, StringComparison.OrdinalIgnoreCase) ? string.Empty : ViewModel.FromAddress ?? string.Empty);
        _avatar.Text = name;
        _avatar.CustomImage = pictures.Get(ViewModel.FromAddress);
    }

    private void UpdateRecipients()
    {
        var lines = new List<string>();
        void Add(string label, IEnumerable<AccountContactViewModel> contacts)
        {
            var names = string.Join(", ", contacts.Select(static contact => contact.DisplayName).Where(static name => !string.IsNullOrWhiteSpace(name)));
            if (names.Length > 0) lines.Add($"{label} {names}");
        }
        Add(Translator.ComposerTo.Trim(), ViewModel.ToItems);
        Add("Cc:", ViewModel.CcItems);
        Add("Bcc:", ViewModel.BccItems);
        _recipients.SetText(string.Join("\n", lines));
        _recipients.Visible = lines.Count > 0;
        _date.SetText(MailRowPresentation.FormatReaderDate(ViewModel.CreationDate, ViewModel.PreferencesService.MailTimeFormatPreference));
    }

    private void UpdatePrivacy()
    {
        var vm = ViewModel;
        _withheldBar.Message = vm.WithheldBarMessage;
        _withheldBar.Widget.Visible = vm.IsRemoteContentWithheld;
        _junkBar.Widget.Visible = vm.IsJunkBlocked;
        _ruleBar.Title = vm.RuleBarTitle;
        _ruleBar.Message = vm.RuleBarMessage;
        _ruleBar.Widget.Visible = vm.IsSenderBlockedByRule;
        _privacyBars.Visible = vm.IsRemoteContentBarVisible;
    }

    private void UpdateAttachments()
    {
        _attachmentTiles.RemoveAll();
        var attachments = ViewModel.Attachments.ToArray();
        foreach (var attachment in attachments) _attachmentTiles.Append(CreateTile(attachment));
        _attachmentStrip.Visible = attachments.Length > 0;
        _saveAll.Visible = attachments.Length > 1;
    }

    /// <summary>An attachment tile: the type badge, name and size open it; the save button next to it saves it.</summary>
    private Gtk.Widget CreateTile(MailAttachmentViewModel attachment)
    {
        var extension = Path.GetExtension(attachment.FileName ?? string.Empty).TrimStart('.').ToUpperInvariant();
        if (extension.Length > 4) extension = extension[..4];
        var badge = GtkUi.Label(extension, "wino-attachment-badge", WinoAttachmentClass(extension));
        badge.Xalign = 0.5f;
        badge.SetSizeRequest(34, 34);
        var name = GtkUi.Label(attachment.FileName ?? string.Empty, "heading");
        name.Ellipsize = Pango.EllipsizeMode.Middle;
        name.MaxWidthChars = 24;
        var size = GtkUi.Label(attachment.ReadableSize ?? string.Empty, "dim-label", "caption");
        var open = Gtk.Button.New();
        open.Child = GtkUi.Row(10, badge, GtkUi.Column(1, name, size));
        open.AddCssClass("flat");
        open.TooltipText = $"{Translator.Buttons_Open} {attachment.FileName}";
        open.OnClicked += (_, _) => Observe(ViewModel.OpenAttachmentCommand.ExecuteAsync(attachment));
        var save = Gtk.Button.New();
        save.Child = WinoIcons.Label(WinoIconGlyph.Save, 16);
        save.AddCssClass("flat");
        save.Valign = Gtk.Align.Center;
        save.TooltipText = $"{Translator.Buttons_Save} {attachment.FileName}";
        save.OnClicked += (_, _) => Observe(ViewModel.SaveAttachmentCommand.ExecuteAsync(attachment));
        var tile = GtkUi.Row(0, open, save);
        tile.AddCssClass("card");
        tile.AddCssClass("wino-attachment-tile");
        return tile;
    }

    private static string WinoAttachmentClass(string extension) => extension switch
    {
        "DOC" or "DOCX" => "wino-attachment-word",
        "XLS" or "XLSX" or "CSV" => "wino-attachment-excel",
        "PPT" or "PPTX" => "wino-attachment-powerpoint",
        "PDF" => "wino-attachment-pdf",
        "ZIP" or "RAR" or "7Z" => "wino-attachment-archive",
        _ => "wino-attachment-other"
    };

    private void ShowLinkTarget(string? uri)
    {
        _linkTarget.SetText(uri ?? string.Empty);
        _linkTarget.Visible = !string.IsNullOrEmpty(uri);
    }

    private void ApplyTheme(bool dark)
    {
        if (dark == _dark || _reader is null || _disposedReader) return;
        _dark = dark;
        ViewModel.IsDarkWebviewRenderer = dark;
        Observe(_reader.SetThemeAsync(dark));
    }

    // ---- Rendering ----

    private async Task RenderAsync(string html)
    {
        if (_reader is null || _disposedReader) return;
        await Dispatcher.ExecuteOnUIThread(() => ShowLinkTarget(null));
        _currentRenderedHtml = html ?? string.Empty;
        await _reader.SetThemeAsync(_dark);
        await _reader.SetReaderTypographyAsync($"{ViewModel.PreferencesService.ReaderFont}, sans-serif", ViewModel.PreferencesService.ReaderFontSize);
        var options = ViewModel.CurrentRenderModel?.MailRenderingOptions;
        var policy = options?.RemoteContent == RemoteContentMode.BlockAll ? RemoteContentPolicy.Blocked : RemoteContentPolicy.ImagesAndFontsAllowed;
        await _reader.RenderAsync(new HtmlMailReaderRequest(string.IsNullOrEmpty(_currentRenderedHtml) ? " " : _currentRenderedHtml, policy,
            ViewModel.IsReaderViewEnabled ? HtmlMailRenderMode.Readability : HtmlMailRenderMode.Original, options?.RenderPlaintextLinks ?? true));
        await _reader.SetAccessibilityContextAsync(new ReaderAccessibilityContext(
            string.IsNullOrWhiteSpace(ViewModel.Subject) ? Translator.MailItemNoSubject : ViewModel.Subject,
            string.IsNullOrWhiteSpace(ViewModel.FromName) ? ViewModel.FromAddress : $"{ViewModel.FromName} <{ViewModel.FromAddress}>",
            MailRowPresentation.FormatReaderDate(ViewModel.CreationDate, ViewModel.PreferencesService.MailTimeFormatPreference),
            Translator.Reader_MessageBodyAutomationName,
            Translator.Reader_PlainTextFallbackAutomationName,
            ViewModel.CurrentRenderModel?.AccessibleText ?? string.Empty));
    }

    private Task ClearAsync()
    {
        _currentRenderedHtml = string.Empty;
        return _reader is null || _disposedReader ? Task.CompletedTask : _reader.ClearAsync();
    }

    private void CloseRequested(object? sender, EventArgs args)
        => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Wino.Messaging.Client.Mails.DisposeRenderingFrameRequested());

    private async Task DisposeReaderAsync()
    {
        if (_reader is null || _disposedReader) return;
        _disposedReader = true;
        try { await _reader.DisposeAsync(); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void OnUI(Action action) => _ = Dispatcher.ExecuteOnUIThread(() => { if (!Bindings.IsDisposed) action(); });

    private async void Observe(Task task)
    {
        try { await task; }
        catch (Exception exception) { ReportError(exception); }
    }

    /// <summary>
    /// Debug bridge commands: <c>reader-state</c> (subject, sender, recipients, attachments, privacy bars, whether the
    /// body rendered) and <c>reader-body</c> (the rendered body's text, from the web view).
    /// </summary>
    private void RegisterReaderDebugCommands()
    {
#if DEBUG
        LinuxDebugBridge.Register("reader-state", _ =>
        {
            if (Bindings.IsDisposed) return Task.FromResult("released");
            return Task.FromResult($"subject='{_subject.GetText()}' from='{_senderName.GetText()}' <{ViewModel.FromAddress}> date='{_date.GetText()}' " +
                $"recipients='{_recipients.GetText().Replace('\n', ' ')}' attachments={ViewModel.Attachments.Count} ready={ViewModel.IsMailContentReady} " +
                $"withheld={ViewModel.IsRemoteContentWithheld} bar={ViewModel.IsRemoteContentBarVisible} webkit={_webView is not null} rendered={_currentRenderedHtml.Length}");
        });
        LinuxDebugBridge.Register("reader-body", async _ =>
        {
            if (_webView is null || Bindings.IsDisposed) return "no web view";
            var text = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.ExecuteOnUIThread(async () =>
            {
                try
                {
                    var value = await _webView.EvaluateJavascriptAsync(
                        "String((document.getElementById('wino-reader')?.shadowRoot?.textContent ?? document.body.innerText) || '').replace(/\\s+/g,' ').trim().slice(0,300)");
                    text.TrySetResult(value.ToString());
                }
                catch (Exception exception) { text.TrySetResult("error: " + exception.Message); }
            });
            return await text.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
#endif
    }
}
