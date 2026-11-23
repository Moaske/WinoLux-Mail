using Microsoft.Extensions.DependencyInjection;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models;
using Wino.Core.ViewModels;
using Wino.Mail.Linux.Infrastructure;

namespace Wino.Mail.Linux.Views.Shell;

/// <summary>
/// The shared keyboard shortcuts (Settings › Keyboard Shortcuts) for the active mode, as the Mac AppDelegate.Shortcuts
/// dispatches them: a key press in the window that matches an enabled shortcut and passes
/// <see cref="KeyboardShortcutContextPolicy"/> goes to the content page's ViewModel, then to the mode provider. The
/// window sees keys before the focused widget (capture phase), so the list does not swallow Delete; the policy keeps
/// a bare Delete and the clipboard keys for text fields. Keys in a dialog or a popover menu belong to it.
/// </summary>
public sealed partial class WinoAppShellPage
{
    /// <summary>Shortcuts that open the composer, which arrives with milestone 2g; until then they would create a draft
    /// with nowhere to show it.</summary>
    private static readonly HashSet<KeyboardShortcutAction> ComposerShortcuts =
        [KeyboardShortcutAction.NewMail, KeyboardShortcutAction.Reply, KeyboardShortcutAction.ReplyAll, KeyboardShortcutAction.Send];

    private Gtk.EventControllerKey? _shortcutKeys;
    private bool _shortcutRunning;

    private void AttachShortcuts()
    {
        _shortcutKeys = Gtk.EventControllerKey.New();
        _shortcutKeys.PropagationPhase = Gtk.PropagationPhase.Capture;
        Gtk.EventControllerKey.KeyPressedSignal.Connect(_shortcutKeys, (_, args) => TryShortcut(args.Keyval, args.State));
        _window.AddController(_shortcutKeys);
#if DEBUG
        LinuxDebugBridge.Register("shortcut", async args =>
        {
            // shortcut KEY [Control] [Shift] [Alt]: runs the matching shortcut as if pressed in the list.
            if (args.Length == 0) return "usage: shortcut KEY [Control] [Shift] [Alt]";
            var modifiers = ModifierKeys.None;
            foreach (var arg in args.Skip(1))
                if (Enum.TryParse<ModifierKeys>(arg, true, out var modifier)) modifiers |= modifier;
            var result = "no match";
            await Dispatcher.ExecuteOnUIThread(() =>
            {
                if (Match(args[0].ToUpperInvariant(), modifiers, isTextInput: false) is { } shortcut)
                {
                    Route(shortcut, null);
                    result = $"ran {shortcut.Action}";
                }
            });
            return result;
        });
#endif
    }

    private void DetachShortcuts()
    {
        if (_shortcutKeys is null) return;
        _window.RemoveController(_shortcutKeys);
        _shortcutKeys = null;
    }

    private bool TryShortcut(uint keyval, Gdk.ModifierType state)
    {
        if (_window.HasVisibleDialog || KeyName(keyval) is not { } key) return false;
        var modifiers = ModifierKeys.None;
        if ((state & Gdk.ModifierType.ControlMask) != 0) modifiers |= ModifierKeys.Control;
        if ((state & Gdk.ModifierType.ShiftMask) != 0) modifiers |= ModifierKeys.Shift;
        if ((state & Gdk.ModifierType.AltMask) != 0) modifiers |= ModifierKeys.Alt;
        if ((state & Gdk.ModifierType.SuperMask) != 0) modifiers |= ModifierKeys.Windows;
        var focus = _window.Focus;
        if (Match(key, modifiers, IsTextInput(focus)) is not { } shortcut) return false;
        Route(shortcut, focus);
        return true;
    }

    /// <summary>The enabled shortcut of the active mode for the key, when the input context allows it.</summary>
    private KeyboardShortcutSnapshot? Match(string key, ModifierKeys modifiers, bool isTextInput)
    {
        var mode = ViewModel.CurrentMode;
        if (mode != WinoApplicationMode.Mail || _child is null) return null;
        var service = _services.GetService<IKeyboardShortcutService>();
        if (service is null) return null;
        foreach (var candidate in service.EnabledShortcutsSnapshot)
        {
            if (candidate.Mode != mode || candidate.ModifierKeys != modifiers || !KeyMatches(candidate.Key, key)) continue;
            if (ComposerShortcuts.Contains(candidate.Action)) return null;
            return KeyboardShortcutContextPolicy.CanExecute(candidate.Action, candidate.Key ?? string.Empty, candidate.ModifierKeys,
                KeyboardShortcutInputContext.List, isTextInput) ? candidate : null;
        }
        return null;
    }

    private async void Route(KeyboardShortcutSnapshot shortcut, Gtk.Widget? origin)
    {
        if (_shortcutRunning) return;
        _shortcutRunning = true;
        try
        {
            var details = new KeyboardShortcutTriggerDetails
            {
                ShortcutId = shortcut.Id,
                Mode = shortcut.Mode,
                Action = shortcut.Action,
                Key = shortcut.Key ?? string.Empty,
                ModifierKeys = shortcut.ModifierKeys,
                InputContext = KeyboardShortcutInputContext.List,
                Sender = _window,
                Origin = origin!
            };
            if (_child?.AssociatedViewModel is CoreBaseViewModel page) await page.KeyboardShortcutHook(details);
            if (!details.Handled) await ViewModel.KeyboardShortcutHookForMode(details);
        }
        catch (Exception exception) { ReportError(exception); }
        finally { _shortcutRunning = false; }
    }

    /// <summary>Text entry has focus: an entry's text, a text view, or anything inside an editable.</summary>
    private static bool IsTextInput(Gtk.Widget? focus)
    {
        for (var widget = focus; widget is not null; widget = widget.GetParent())
            if (widget is Gtk.Text or Gtk.TextView or Gtk.Editable) return true;
        return false;
    }

    /// <summary>The stored key name (Windows key names, as the shared service normalizes them) for a GDK key.</summary>
    private static string? KeyName(uint keyval)
    {
        var name = Gdk.Functions.KeyvalName(Gdk.Functions.KeyvalToUpper(keyval));
        return name switch
        {
            null => null,
            "Return" or "KP_Enter" => "ENTER",
            "Delete" or "KP_Delete" => "DELETE",
            "BackSpace" => "BACK",
            "Escape" => "ESCAPE",
            "space" => "SPACE",
            "Tab" or "ISO_Left_Tab" => "TAB",
            "Shift_L" or "Shift_R" or "Control_L" or "Control_R" or "Alt_L" or "Alt_R" or "Super_L" or "Super_R" or "Meta_L" or "Meta_R" => null,
            _ => name.ToUpperInvariant()
        };
    }

    /// <summary>Digits are stored as "NUMBER1" by Windows and "1" elsewhere; both match.</summary>
    private static bool KeyMatches(string? stored, string pressed)
    {
        var key = stored?.Trim().ToUpperInvariant() ?? string.Empty;
        return key == pressed || (pressed.Length == 1 && char.IsAsciiDigit(pressed[0]) && key == "NUMBER" + pressed);
    }
}
