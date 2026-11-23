using System.ComponentModel;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Models.MailItem;
using Wino.Core.Domain.Presentation;
using Wino.Mail.Controls.Core;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels.Data;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// Mail actions (the Mac MailListPageViewController.Commands and its toolbar): the reader column's header bar carries
/// Archive, Delete, Move · Flag, Mark read · More in the Windows command order, acting on the selection like the Mac
/// unified toolbar; the context menu on a row (secondary click, the Menu key or Shift+F10) offers the shared actions
/// for the clicked rows, Pin and the thread toggle. Everything runs through the shared ViewModel, so operations,
/// confirmations and the Move folder picker behave as on the other platforms. Reply, Reply all and Forward stay out
/// until the composer exists (milestone 2g): they would create a draft with nowhere to show it.
/// </summary>
public sealed partial class MailListPage
{
    private static readonly HashSet<MailOperation> ComposerOperations = [MailOperation.Reply, MailOperation.ReplyAll, MailOperation.Forward];
    private Gtk.Button _archiveButton = null!;
    private Gtk.Button _deleteButton = null!;
    private Gtk.Button _moveButton = null!;
    private Gtk.Button _flagButton = null!;
    private Gtk.Button _readButton = null!;
    private Gtk.Button _moreButton = null!;
    private Gtk.Box _readerCommands = null!;
    private readonly List<INotifyPropertyChanged> _watchedSelection = [];
    private bool _commandsQueued;

    private void BuildReaderCommands(Adw.HeaderBar header)
    {
        _archiveButton = CommandButton(() => RunSelectionOperation(ViewModel.IsArchiveSpecialFolder ? MailOperation.UnArchive : MailOperation.Archive));
        _deleteButton = CommandButton(() => RunSelectionOperation(MailOperation.SoftDelete));
        _moveButton = CommandButton(() => RunSelectionOperation(MailOperation.Move));
        _flagButton = CommandButton(() => RunSelectionOperation(AllSelected(static item => item.IsFlagged) ? MailOperation.ClearFlag : MailOperation.SetFlag));
        _readButton = CommandButton(() => RunSelectionOperation(AllSelected(static item => item.IsRead) ? MailOperation.MarkAsUnread : MailOperation.MarkAsRead));
        _moreButton = CommandButton(ShowMoreMenu);
        SetCommand(_moveButton, MailOperation.Move);
        SetCommand(_deleteButton, MailOperation.SoftDelete);
        _moreButton.Child = WinoIcons.Label(WinoIconGlyph.More, 16);
        _moreButton.TooltipText = Translator.More;

        var file = GtkUi.Row(0, _archiveButton, _deleteButton, _moveButton);
        file.AddCssClass("linked");
        var mark = GtkUi.Row(0, _flagButton, _readButton);
        mark.AddCssClass("linked");
        _readerCommands = GtkUi.Row(8, file, mark, _moreButton);
        header.PackStart(_readerCommands);
        UpdateMailCommands();
        RegisterCommandDebugCommands();
    }

    /// <summary>
    /// Debug bridge commands: <c>mail-command archive|delete|move|flag|read|more</c> clicks the reader header button
    /// (answers the buttons' tooltips first), and <c>mail-context N</c> opens the context menu on the Nth mail row and
    /// answers its items.
    /// </summary>
    private void RegisterCommandDebugCommands()
    {
#if DEBUG
        Infrastructure.LinuxDebugBridge.Register("mail-command", args =>
        {
            if (_released) return Task.FromResult("released");
            var buttons = new Dictionary<string, Gtk.Button>
            {
                ["archive"] = _archiveButton, ["delete"] = _deleteButton, ["move"] = _moveButton,
                ["flag"] = _flagButton, ["read"] = _readButton, ["more"] = _moreButton
            };
            var state = $"visible={_readerCommands.Visible} " + string.Join(" ", buttons.Select(pair => $"{pair.Key}='{pair.Value.TooltipText}'"));
            if (args.Length == 0) return Task.FromResult(state);
            if (!buttons.TryGetValue(args[0], out var button)) return Task.FromResult("unknown command");
            button.Activate();
            return Task.FromResult("clicked " + args[0] + " :: " + state);
        });
        Infrastructure.LinuxDebugBridge.Register("mail-context", args =>
        {
            if (_released) return Task.FromResult("released");
            int ordinal = args.Length > 0 && int.TryParse(args[0], out var value) ? value : 0;
            int seen = -1;
            var entry = _entries.FirstOrDefault(candidate => candidate.Row is not null && ++seen == ordinal);
            var slot = entry is null ? null : _slots.FirstOrDefault(candidate => candidate.Key == entry.Key && candidate.Widget.GetMapped());
            if (slot is null) return Task.FromResult("row not shown");
            ShowContextMenu(slot);
            return Task.FromResult("menu: " + LastMenuDescription);
        });
#endif
    }

    private static Gtk.Button CommandButton(Action clicked)
    {
        var button = Gtk.Button.New();
        button.AddCssClass("flat");
        button.OnClicked += (_, _) => clicked();
        return button;
    }

    private static void SetCommand(Gtk.Button button, MailOperation operation)
    {
        button.Child = WinoIcons.Label(MailOperationDisplay.GetGlyph(operation), 16);
        button.TooltipText = MailOperationDisplay.GetTitle(operation);
    }

    private bool AllSelected(Func<MailItemViewModel, bool> predicate)
        => ViewModel.SelectedItemsCount > 0 && ViewModel.SelectedItems.All(predicate);

    private void RunSelectionOperation(MailOperation operation)
    {
        if (ViewModel.SelectedItemsCount == 0) return;
        Observe(RunAndRefreshAsync(ViewModel.ExecuteMailOperationCommand.ExecuteAsync(operation)));
    }

    private async Task RunAndRefreshAsync(Task operation)
    {
        try { await operation; }
        finally { OnUI(QueueCommandUpdate); }
    }

    /// <summary>
    /// The header shows the commands while messages are selected; Flag and Mark read show the action they will take,
    /// like the Windows toggle buttons. Re-runs when the selection or a selected message's read or flag state changes.
    /// </summary>
    private void UpdateMailCommands()
    {
        if (_readerCommands is null) return;
        foreach (var watched in _watchedSelection) watched.PropertyChanged -= SelectedItemChanged;
        _watchedSelection.Clear();
        bool any = !_released && ViewModel.SelectedItemsCount > 0;
        _readerCommands.Visible = any;
        if (!any) return;
        foreach (var item in ViewModel.SelectedItems)
        {
            item.PropertyChanged += SelectedItemChanged;
            _watchedSelection.Add(item);
        }
        SetCommand(_archiveButton, ViewModel.IsArchiveSpecialFolder ? MailOperation.UnArchive : MailOperation.Archive);
        SetCommand(_flagButton, AllSelected(static item => item.IsFlagged) ? MailOperation.ClearFlag : MailOperation.SetFlag);
        SetCommand(_readButton, AllSelected(static item => item.IsRead) ? MailOperation.MarkAsUnread : MailOperation.MarkAsRead);
    }

    private void SelectedItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MailItemViewModel.IsRead) or nameof(MailItemViewModel.IsFlagged) or null)
            OnUI(QueueCommandUpdate);
    }

    private void QueueCommandUpdate()
    {
        if (_commandsQueued) return;
        _commandsQueued = true;
        GLib.Functions.IdleAdd(DefaultIdlePriority, () =>
        {
            _commandsQueued = false;
            if (!_released) UpdateMailCommands();
            return false;
        });
    }

    private void ReleaseMailCommands()
    {
        foreach (var watched in _watchedSelection) watched.PropertyChanged -= SelectedItemChanged;
        _watchedSelection.Clear();
    }

    // ---- More and the context menu ----

    private void ShowMoreMenu()
    {
        var targets = ViewModel.SelectedItems.ToArray();
        if (targets.Length == 0) return;
        ShowActionMenu(_moreButton, targets, row: null);
    }

    /// <summary>The context menu for the row in <paramref name="slot"/> (Mac BuildContextMenu): the clicked thread's
    /// messages, the selection when the row is selected, or the row alone.</summary>
    private void ShowContextMenu(MailRowView slot)
    {
        if (!_entryByKey.TryGetValue(slot.Key, out var entry) || entry.Row is not { } row) return;
        var item = (MailItemViewModel)row.SourceItem;
        IReadOnlyList<MailItemViewModel> targets = row.IsThreadHead
            ? row.LeafItems.OfType<MailItemViewModel>().ToArray()
            : ViewModel.IsMailSelected(item.UniqueId) ? ViewModel.SelectedItems.ToArray() : [item];
        if (targets.Count == 0) return;
        ShowActionMenu(slot.Widget, targets, row);
    }

    /// <summary>The last menu's items, for the debug bridge.</summary>
    private string LastMenuDescription { get; set; } = string.Empty;

    private void ShowActionMenu(Gtk.Widget anchor, IReadOnlyList<MailItemViewModel> targets, MailListRow? row)
    {
        var menu = new ActionMenu();
        foreach (var action in ViewModel.GetAvailableMailActions(targets))
        {
            if (action.Operation == MailOperation.Seperator) { menu.Separator(); continue; }
            if (ComposerOperations.Contains(action.Operation)) continue;
            var title = MailOperationDisplay.GetTitle(action.Operation);
            if (string.IsNullOrEmpty(title)) continue;
            var operation = action.Operation;
            menu.Add(operation == MailOperation.Move ? $"{title}…" : title, action.IsEnabled, () => RunOperation(operation, targets));
        }
        menu.Separator();
        bool allPinned = targets.All(static target => target.MailCopy.IsPinned);
        menu.Add(allPinned ? Translator.FolderOperation_Unpin : Translator.FolderOperation_Pin, true,
            () => Observe(ViewModel.ChangePinnedStatusAsync(targets, !allPinned)));
        if (row is { IsThreadHead: true, Thread: { Count: > 1 } })
        {
            menu.Separator();
            menu.Add(row.IsExpanded ? Translator.MacOS_MailList_CollapseThread : Translator.MacOS_MailList_ExpandThread, true, () => ToggleThread(row));
        }
        LastMenuDescription = menu.Description;
        menu.Popup(anchor);
    }

    private void RunOperation(MailOperation operation, IReadOnlyList<MailItemViewModel> items)
    {
        if (items.Count == 0) return;
        Observe(RunAndRefreshAsync(ViewModel.ExecuteMailOperationAsync(
            new MailOperationPreperationRequest(operation, items.Select(static item => item.MailCopy)))));
    }

    /// <summary>The context menu's anchor for the keyboard: the first selected row's slot, when it is realized.</summary>
    private MailRowView? SelectedSlot()
    {
        var first = FirstSelectedPosition();
        if (first < 0 || first >= _entries.Count) return null;
        var key = _entries[first].Key;
        return _slots.FirstOrDefault(slot => slot.Key == key && slot.Widget.GetMapped());
    }

    /// <summary>The slot under a point of the list view (the picked widget or one of its ancestors).</summary>
    private MailRowView? SlotAt(double x, double y)
    {
        for (var widget = _listView.Pick(x, y, Gtk.PickFlags.Default); widget is not null; widget = widget.GetParent())
        {
            var name = widget.Name;
            if (name is not null && name.StartsWith("mail-slot-", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan("mail-slot-".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                && index < _slots.Count)
                return _slots[index];
        }
        return null;
    }

    private void AttachContextMenu()
    {
        var click = Gtk.GestureClick.New();
        click.Button = 3;
        click.OnPressed += (_, args) =>
        {
            if (SlotAt(args.X, args.Y) is not { } slot) return;
            // A secondary click on an unselected row acts on that row alone (Windows and GNOME Files behave alike).
            ShowContextMenu(slot);
        };
        _listView.AddController(click);

        var keys = Gtk.EventControllerKey.New();
        Gtk.EventControllerKey.KeyPressedSignal.Connect(keys, (_, args) =>
        {
            bool shift = (args.State & Gdk.ModifierType.ShiftMask) != 0;
            bool menuKey = args.Keyval == Gdk.Constants.KEY_Menu || (shift && args.Keyval == Gdk.Constants.KEY_F10);
            if (!menuKey || SelectedSlot() is not { } slot) return false;
            ShowContextMenu(slot);
            return true;
        });
        _listView.AddController(keys);
    }

    /// <summary>
    /// A GNOME popover menu built from plain items: each item becomes a <c>GSimpleAction</c> in a group inserted on the
    /// anchor, and separators start a new menu section.
    /// </summary>
    private sealed class ActionMenu
    {
        private readonly Gio.Menu _menu = Gio.Menu.New();
        private readonly Gio.SimpleActionGroup _actions = Gio.SimpleActionGroup.New();
        private Gio.Menu _section = Gio.Menu.New();
        private int _count;
        private readonly List<string> _titles = [];

        public string Description => string.Join(" | ", _titles);

        public void Add(string title, bool enabled, Action invoked)
        {
            _titles.Add(enabled ? title : $"({title})");
            var name = "item" + (_count++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var action = Gio.SimpleAction.New(name, null);
            action.Enabled = enabled;
            action.OnActivate += (_, _) => invoked();
            _actions.AddAction(action);
            _section.Append(title, "winomail." + name);
        }

        public void Separator()
        {
            if (_section.GetNItems() == 0) return;
            _titles.Add("—");
            _menu.AppendSection(null, _section);
            _section = Gio.Menu.New();
        }

        public void Popup(Gtk.Widget anchor)
        {
            Separator();
            if (_menu.GetNItems() == 0) return;
            anchor.InsertActionGroup("winomail", _actions);
            var popover = Gtk.PopoverMenu.NewFromModel(_menu);
            popover.SetParent(anchor);
            popover.HasArrow = true;
            popover.OnClosed += (_, _) =>
            {
                // Unparent after the activated action ran: the menu activates its item before it reports closing.
                GLib.Functions.IdleAdd(DefaultIdlePriority, () =>
                {
                    popover.Unparent();
                    anchor.InsertActionGroup("winomail", null);
                    return false;
                });
            };
            popover.Popup();
        }
    }
}
