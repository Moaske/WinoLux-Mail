namespace Wino.Mail.Linux.Presentation;

/// <summary>Small libadwaita building blocks the pages share.</summary>
public static class GtkUi
{
    public static Gtk.Label Label(string text, params string[] cssClasses)
    {
        var label = Gtk.Label.New(text);
        label.Wrap = true;
        label.Xalign = 0;
        foreach (var cssClass in cssClasses) label.AddCssClass(cssClass);
        return label;
    }

    public static Gtk.Box Column(int spacing, params Gtk.Widget[] children)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, spacing);
        foreach (var child in children) box.Append(child);
        return box;
    }

    public static Gtk.Box Row(int spacing, params Gtk.Widget[] children)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, spacing);
        foreach (var child in children) box.Append(child);
        return box;
    }

    public static Gtk.Widget Spacer()
    {
        var spacer = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        spacer.Hexpand = true;
        return spacer;
    }

    /// <summary>A preferences row: title (and subtitle) with <paramref name="suffix"/> on the trailing side.</summary>
    public static Adw.ActionRow FieldRow(string title, Gtk.Widget suffix, string? subtitle = null)
    {
        var row = Adw.ActionRow.New();
        row.Title = title;
        if (!string.IsNullOrEmpty(subtitle)) row.Subtitle = subtitle;
        row.AddSuffix(suffix);
        return row;
    }

    /// <summary>
    /// A scrolling onboarding page: centred content up to <paramref name="maximumWidth"/> wide, and a footer bar with
    /// <paramref name="start"/> widgets on the leading side and <paramref name="end"/> widgets on the trailing side.
    /// </summary>
    public static Gtk.Widget Page(Gtk.Widget content, IEnumerable<Gtk.Widget> start, IEnumerable<Gtk.Widget> end, int maximumWidth = 640)
    {
        var clamp = Adw.Clamp.New();
        clamp.MaximumSize = maximumWidth;
        clamp.Child = content;
        clamp.MarginTop = 24;
        clamp.MarginBottom = 24;
        clamp.MarginStart = 16;
        clamp.MarginEnd = 16;

        var scroller = Gtk.ScrolledWindow.New();
        scroller.HscrollbarPolicy = Gtk.PolicyType.Never;
        scroller.Vexpand = true;
        scroller.Child = clamp;

        var footer = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        footer.MarginTop = 12;
        footer.MarginBottom = 12;
        footer.MarginStart = 16;
        footer.MarginEnd = 16;
        foreach (var widget in start) footer.Append(widget);
        footer.Append(Spacer());
        foreach (var widget in end) footer.Append(widget);

        var toolbar = Adw.ToolbarView.New();
        toolbar.Content = scroller;
        toolbar.AddBottomBar(footer);
        toolbar.BottomBarStyle = Adw.ToolbarStyle.Raised;
        return toolbar;
    }

    /// <summary>A page heading: title and an optional description.</summary>
    public static Gtk.Box Heading(Gtk.Label title, Gtk.Label? description)
    {
        title.AddCssClass("title-2");
        var box = Column(6, title);
        if (description is not null)
        {
            description.AddCssClass("dim-label");
            box.Append(description);
        }
        return box;
    }
}

/// <summary>The Windows InfoBar inside a page: severity colour, title and message, optionally closable.</summary>
public sealed class InfoBar
{
    private readonly Gtk.Label _title;
    private readonly Gtk.Label _message;

    public InfoBar(InfoBarSeverity severity, string? title = null, string? message = null)
    {
        _title = GtkUi.Label(title ?? string.Empty, "heading");
        _title.Visible = !string.IsNullOrEmpty(title);
        _message = GtkUi.Label(message ?? string.Empty);
        _message.Visible = !string.IsNullOrEmpty(message);
        _message.Selectable = true;
        var icon = Gtk.Image.NewFromIconName(severity switch
        {
            InfoBarSeverity.Error => "dialog-error-symbolic",
            InfoBarSeverity.Warning => "dialog-warning-symbolic",
            InfoBarSeverity.Success => "emblem-ok-symbolic",
            _ => "dialog-information-symbolic"
        });
        icon.Valign = Gtk.Align.Start;
        var text = GtkUi.Column(2, _title, _message);
        text.Hexpand = true;
        Widget = GtkUi.Row(12, icon, text);
        Widget.AddCssClass("card");
        Widget.AddCssClass(severity switch
        {
            InfoBarSeverity.Error => "error",
            InfoBarSeverity.Warning => "warning",
            InfoBarSeverity.Success => "success",
            _ => "accent"
        });
        Widget.MarginTop = Widget.MarginBottom = 0;
        foreach (var child in new Gtk.Widget[] { icon, text })
        {
            child.MarginTop = 12;
            child.MarginBottom = 12;
        }
        icon.MarginStart = 12;
        text.MarginEnd = 12;
    }

    public Gtk.Box Widget { get; }

    public string Title
    {
        set { _title.SetText(value ?? string.Empty); _title.Visible = !string.IsNullOrEmpty(value); }
    }

    public string Message
    {
        set { _message.SetText(value ?? string.Empty); _message.Visible = !string.IsNullOrEmpty(value); }
    }
}

public enum InfoBarSeverity
{
    Informational,
    Success,
    Warning,
    Error
}
