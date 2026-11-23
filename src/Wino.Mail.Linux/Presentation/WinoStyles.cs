namespace Wino.Mail.Linux.Presentation;

/// <summary>
/// The few styles Wino draws itself on top of libadwaita (design decisions, Principles 2): icon glyph sizes, unread
/// pills, sidebar section headers and the mail rows (Windows unread, flag and attachment colours). Colours come from libadwaita's named colours, so light, dark and the system
/// accent need nothing here.
/// </summary>
internal static class WinoStyles
{
    private const string Css = $$"""
        .wino-icon { font-family: "{{WinoIcons.FontFamily}}"; font-weight: normal; font-style: normal; }
        .wino-icon-14 { font-size: 14px; }
        .wino-icon-16 { font-size: 16px; }
        .wino-icon-20 { font-size: 20px; }
        .wino-pill { font-size: 0.8em; font-weight: bold; font-feature-settings: "tnum"; padding: 1px 7px; border-radius: 99px; background-color: alpha(currentColor, 0.1); }
        .wino-sidebar-header { font-size: 0.8em; font-weight: bold; opacity: 0.6; margin-top: 10px; }
        .wino-sidebar-row { min-height: 34px; }
        .wino-account-row { min-height: 40px; }
        .wino-mode-switcher { margin: 6px; }
        .wino-mail-list > row { padding: 0; }
        .wino-mail-group { font-size: 0.8em; font-weight: bold; opacity: 0.7; margin-top: 14px; margin-bottom: 6px; }
        .wino-mail-row.wino-unread .wino-mail-sender { font-weight: bold; }
        .wino-mail-row.wino-unread .wino-mail-subject { font-weight: 600; color: @accent_color; }
        .wino-mail-preview, .wino-mail-date { opacity: 0.7; }
        .wino-mail-draft { color: @error_color; }
        .wino-mail-attachment { color: #FDCB6E; }
        .wino-mail-flag { color: #E74C3C; }
        .wino-mail-unread-dot { background-color: @accent_bg_color; border-radius: 3px; min-width: 6px; min-height: 6px; }
        .wino-mail-chevron { min-width: 18px; min-height: 18px; padding: 0; }
        .wino-reader-card { background-color: @view_bg_color; }
        .wino-link-target { background-color: @popover_bg_color; color: @popover_fg_color; padding: 2px 8px; margin: 6px; border-radius: 6px; font-size: 0.85em; box-shadow: 0 1px 3px alpha(black, 0.25); }
        .wino-attachment-tile { padding: 2px; }
        .wino-attachment-badge { color: white; font-size: 0.75em; font-weight: bold; border-radius: 6px; }
        .wino-attachment-word { background-color: #2B579A; }
        .wino-attachment-excel { background-color: #217346; }
        .wino-attachment-powerpoint { background-color: #B7472A; }
        .wino-attachment-pdf { background-color: #D13438; }
        .wino-attachment-archive { background-color: #795548; }
        .wino-attachment-other { background-color: #6E6E73; }
        """;

    private static Gtk.CssProvider? _provider;

    /// <summary>Installs the styles for the default display. UI thread, once the display exists.</summary>
    public static void Install()
    {
        if (_provider is not null || Gdk.Display.GetDefault() is not { } display) return;
        _provider = Gtk.CssProvider.New();
        _provider.LoadFromString(Css);
        Gtk.StyleContext.AddProviderForDisplay(display, _provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
    }
}
