namespace Wino.Mail.Linux.Presentation;

/// <summary>
/// The few styles Wino draws itself on top of libadwaita (design decisions, Principles 2): icon glyph sizes, unread
/// pills and sidebar section headers. Colours come from libadwaita's named colours, so light, dark and the system
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
