using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Platform.Linux;

namespace Wino.Mail.Linux.Presentation;

/// <summary>
/// The WinoIcons Material Rounded font (icons/README.md, decision L1) as GTK labels. Glyphs come from the shared
/// <see cref="WinoIconGlyphs"/> map, so every head shows the same icon for a concept. GTK twin of the Android and
/// AppKit <c>WinoIcons</c>; the CSS classes are in <see cref="WinoStyles"/>.
/// </summary>
internal static class WinoIcons
{
    public const string FontFamily = "WinoIconsMaterialRounded";

    /// <summary>Registers the shipped font. Call before GTK starts.</summary>
    public static bool Register(string packagedResourceRoot)
        => LinuxFonts.AddApplicationFont(Path.Combine(packagedResourceRoot, "Fonts", "WinoIcons.ttf"));

    public static string Glyph(WinoIconGlyph icon) => icon == WinoIconGlyph.None ? string.Empty : WinoIconGlyphs.GetGlyph(icon);

    /// <summary>A label showing <paramref name="icon"/> at 14, 16 or 20 pixels. The row or button around it names the action.</summary>
    public static Gtk.Label Label(WinoIconGlyph icon, int size = 16)
    {
        var label = Gtk.Label.New(Glyph(icon));
        label.AddCssClass("wino-icon");
        label.AddCssClass(size switch { <= 14 => "wino-icon-14", >= 20 => "wino-icon-20", _ => "wino-icon-16" });
        label.Halign = Gtk.Align.Center;
        label.Valign = Gtk.Align.Center;
        return label;
    }

    public static void Set(Gtk.Label label, WinoIconGlyph icon) => label.SetText(Glyph(icon));

    /// <summary>A flat icon button with a Wino glyph and a tooltip that also names it for screen readers.</summary>
    public static Gtk.Button Button(WinoIconGlyph icon, string tooltip, int size = 16)
    {
        var button = Gtk.Button.New();
        button.Child = Label(icon, size);
        button.TooltipText = tooltip;
        button.AddCssClass("flat");
        return button;
    }
}
