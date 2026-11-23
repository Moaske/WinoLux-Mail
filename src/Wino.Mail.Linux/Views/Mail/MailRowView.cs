using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Mail.Controls.Core;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels.Collections;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>What a mail row shows: the shared row content plus the head's display options and picture.</summary>
internal sealed record MailRowModel(MailRowContent Content, WinoMailRowDensity Density, bool ShowPicture, bool ShowPreview, Gdk.Texture? Picture);

/// <summary>
/// One slot of the virtualised mail list: a date group header or a mail row, whichever the bound entry is. The row
/// follows the Windows MailItemDisplayInformationControl lines, as the AppKit WinoMailRowView does: sender with the
/// draft tag, thread count and the pin, attachment and flag glyphs; subject with the date and the unread dot; the
/// preview. Unread rows draw the sender bold and the subject in the accent colour. Sizes come from the shared
/// <see cref="WinoMailRowMetrics"/>.
/// </summary>
internal sealed class MailRowView
{
    private readonly Gtk.Label _header = GtkUi.Label(string.Empty, "wino-mail-group");
    private readonly Gtk.Box _row = Gtk.Box.New(Gtk.Orientation.Horizontal, (int)WinoMailRowMetrics.AvatarSpacing);
    private readonly Adw.Avatar _avatar = Adw.Avatar.New(32, null, true);
    private readonly Gtk.Label _draft = SingleLine("wino-mail-draft");
    private readonly Gtk.Label _sender = SingleLine("wino-mail-sender");
    private readonly Gtk.Label _threadCount = SingleLine("dim-label", "caption");
    private readonly Gtk.Label _pin = WinoIcons.Label(WinoIconGlyph.Pin, 14);
    private readonly Gtk.Label _attachment = WinoIcons.Label(WinoIconGlyph.Attachment, 14);
    private readonly Gtk.Label _flag = WinoIcons.Label(WinoIconGlyph.Flag, 14);
    private readonly Adw.Spinner _busy = Adw.Spinner.New();
    private readonly Gtk.Button _chevron = Gtk.Button.New();
    private readonly Gtk.Label _chevronGlyph = WinoIcons.Label(WinoIconGlyph.ChevronRight, 14);
    private readonly Gtk.Label _subject = SingleLine("wino-mail-subject");
    private readonly Gtk.Label _date = SingleLine("wino-mail-date", "caption");
    private readonly Gtk.Box _unreadDot = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
    private readonly Gtk.Label _preview = SingleLine("wino-mail-preview");

    public MailRowView()
    {
        _pin.AddCssClass("dim-label");
        _attachment.AddCssClass("wino-mail-attachment");
        _flag.AddCssClass("wino-mail-flag");
        _busy.SetSizeRequest(12, 12);
        _sender.Hexpand = true;
        _subject.Hexpand = true;
        _preview.Hexpand = true;
        _unreadDot.AddCssClass("wino-mail-unread-dot");
        _unreadDot.SetSizeRequest(6, 6);
        _unreadDot.Valign = Gtk.Align.Center;
        _chevron.Child = _chevronGlyph;
        _chevron.AddCssClass("flat");
        _chevron.AddCssClass("wino-mail-chevron");
        _chevron.Valign = Gtk.Align.Center;
        _chevron.OnClicked += (_, _) => ThreadToggleRequested?.Invoke(this, EventArgs.Empty);
        _avatar.Valign = Gtk.Align.Center;

        var line1 = GtkUi.Row(4, _draft, _sender, _threadCount, _pin, _attachment, _flag, _busy);
        var line2 = GtkUi.Row(8, _chevron, _subject, _date, _unreadDot);
        var text = GtkUi.Column((int)WinoMailRowMetrics.LineSpacing, line1, line2, _preview);
        text.Hexpand = true;
        text.Valign = Gtk.Align.Center;
        _row.Append(_avatar);
        _row.Append(text);
        _row.AddCssClass("wino-mail-row");
        _row.MarginStart = (int)WinoMailRowMetrics.InnerLeading;
        _row.MarginEnd = (int)WinoMailRowMetrics.InnerTrailing;

        _header.MarginStart = (int)WinoMailRowMetrics.InnerLeading;
        Widget = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        Widget.Append(_header);
        Widget.Append(_row);
    }

    /// <summary>The slot's widget, the child of its list item.</summary>
    public Gtk.Box Widget { get; }

    /// <summary>The thread chevron was clicked.</summary>
    public event EventHandler? ThreadToggleRequested;

    /// <summary>Unsubscribes the bound mail items; set by the list when it binds the slot.</summary>
    public IDisposable? Subscription
    {
        get;
        set
        {
            field?.Dispose();
            field = value;
        }
    }

    /// <summary>The list entry the slot shows (its selection key).</summary>
    public string Key { get; set; } = string.Empty;

    public void ShowHeader(string title)
    {
        _header.SetText(title);
        _header.Visible = true;
        _row.Visible = false;
    }

    public void ShowRow(MailRowModel model)
    {
        _header.Visible = false;
        _row.Visible = true;
        var content = model.Content;
        var density = model.Density;
        bool compact = density == WinoMailRowDensity.Compact;

        int padding = (int)WinoMailRowMetrics.Padding(density);
        _row.MarginTop = _row.MarginBottom = padding;
        _row.SetSizeRequest(-1, (int)WinoMailRowMetrics.MinHeight(density) - 2 * padding);
        _row.MarginStart = (int)(WinoMailRowMetrics.InnerLeading
            + (content.Kind == WinoMailRowKind.ThreadChild ? WinoMailRowMetrics.ChildIndent(density) : 0));

        _avatar.Visible = model.ShowPicture;
        _avatar.Size = (int)WinoMailRowMetrics.AvatarSize(density);
        var name = string.IsNullOrWhiteSpace(content.Sender) ? content.SenderAddress : content.Sender;
        _avatar.Text = name;
        _avatar.CustomImage = model.Picture;

        _draft.SetText($"[{Translator.Draft}]");
        _draft.Visible = content.IsDraft;
        _sender.SetText(content.IsDraft && string.IsNullOrWhiteSpace(content.Sender) ? content.SenderAddress : content.Sender);
        bool thread = content.Kind == WinoMailRowKind.ThreadHead && content.ThreadCount > 1;
        _threadCount.SetText(content.ThreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        _threadCount.Visible = thread;
        _pin.Visible = content.IsPinned;
        _attachment.Visible = content.HasAttachments;
        _flag.Visible = content.IsFlagged;
        _busy.Visible = content.IsBusy;

        _chevron.Visible = thread;
        WinoIcons.Set(_chevronGlyph, content.IsThreadExpanded ? WinoIconGlyph.ChevronDown : WinoIconGlyph.ChevronRight);
        _chevron.TooltipText = content.ThreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _subject.SetText(content.Subject);
        _date.SetText(content.DateText);
        _date.TooltipText = content.AccessibleDateText;
        _unreadDot.Visible = content.IsUnread;
        _preview.SetText(content.Preview);
        _preview.Visible = model.ShowPreview && !compact && !string.IsNullOrWhiteSpace(content.Preview);

        if (content.IsUnread) _row.AddCssClass("wino-unread");
        else _row.RemoveCssClass("wino-unread");
        // Screen readers announce the whole row (sender, subject, date, state), as on the Mac and Windows.
        _row.UpdateProperty([Gtk.AccessibleProperty.Label], [new GObject.Value(content.AccessibilityText)]);
    }

    private static Gtk.Label SingleLine(params string[] cssClasses)
    {
        var label = Gtk.Label.New(string.Empty);
        label.Xalign = 0;
        label.Ellipsize = Pango.EllipsizeMode.End;
        label.SingleLineMode = true;
        foreach (var cssClass in cssClasses) label.AddCssClass(cssClass);
        return label;
    }
}
