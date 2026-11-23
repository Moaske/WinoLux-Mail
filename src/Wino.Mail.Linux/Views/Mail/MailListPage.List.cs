using System.ComponentModel;
using Wino.Core.Domain;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.Controls.Core;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.ViewModels.Collections;
using Wino.Mail.ViewModels.Data;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The message list: the shared <see cref="MailListProjection"/> (date groups, threads) flattened into entries, shown
/// by a virtualised <c>GtkListView</c>. The list model holds only each entry's key in a <c>GtkStringList</c>, so no
/// GObject subclass is needed; a slot looks its entry up by key when GTK binds it. Projection changes are coalesced and
/// applied as one splice of the changed middle, so rows outside it keep their widgets and the scroll position. The
/// selection rules (thread clicks, restoring the selection, auto-select after removal, the shared selection snapshot)
/// follow the Mac MailListPageViewController.List.
/// </summary>
public sealed partial class MailListPage
{
    /// <summary>A list entry: a date group header (Row null) or a projected mail row.</summary>
    private sealed record ListEntry(string Key, MailListRow? Row, string Title);

    private const uint ProjectionThrottleMilliseconds = 120;
    private const double LoadMoreDistance = 600;
    private const int DefaultPriority = 0;
    private const int DefaultIdlePriority = 200;
    private readonly Gtk.StringList _keys = Gtk.StringList.New(null);
    private readonly List<ListEntry> _entries = [];
    private readonly Dictionary<string, ListEntry> _entryByKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectionKeys = new(StringComparer.Ordinal);
    private readonly List<MailRowView> _slots = [];
    private Gtk.MultiSelection _selection = null!;
    private Gtk.ListView _listView = null!;
    private Gtk.ScrolledWindow _scroller = null!;
    private Gtk.Stack _listStack = null!;
    private Adw.StatusPage _emptyStatus = null!;
    private MailListProjection? _projection;
    private bool _listBound;
    private bool _restoringSelection;
    private bool _projectionApplyScheduled;
    private string _lastPublishedSelection = string.Empty;
    private int _lastRowCount;

    private Gtk.Widget BuildListPane()
    {
        _selection = Gtk.MultiSelection.New(_keys);
        _selection.OnSelectionChanged += (_, _) =>
        {
            if (!_restoringSelection) SelectionChanged();
        };

        var factory = Gtk.SignalListItemFactory.New();
        factory.OnSetup += (_, args) =>
        {
            if (args.Object is not Gtk.ListItem item) return;
            var slot = new MailRowView();
            slot.ThreadToggleRequested += (_, _) =>
            {
                if (_entryByKey.TryGetValue(slot.Key, out var entry) && entry.Row is { } row) ToggleThread(row);
            };
            // GTK hands a slot back by its widget; the widget's name finds the C# slot again.
            slot.Widget.Name = "mail-slot-" + _slots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _slots.Add(slot);
            item.Child = slot.Widget;
        };
        factory.OnBind += (_, args) =>
        {
            if (args.Object is not Gtk.ListItem item || SlotOf(item) is not { } slot) return;
            var key = (item.Item as Gtk.StringObject)?.GetString() ?? string.Empty;
            BindSlot(item, slot, key);
        };
        factory.OnUnbind += (_, args) =>
        {
            if (args.Object is Gtk.ListItem item && SlotOf(item) is { } slot) slot.Subscription = null;
        };

        _listView = Gtk.ListView.New(_selection, factory);
        _listView.AddCssClass("wino-mail-list");
        _listView.ShowSeparators = false;
        _scroller = Gtk.ScrolledWindow.New();
        _scroller.HscrollbarPolicy = Gtk.PolicyType.Never;
        _scroller.Vexpand = true;
        _scroller.Child = _listView;
        _scroller.GetVadjustment().OnValueChanged += (_, _) => CheckLoadMore();

        _emptyStatus = Adw.StatusPage.New();
        _emptyStatus.IconName = "mail-unread-symbolic";
        _emptyStatus.AddCssClass("compact");
        var spinner = Adw.Spinner.New();
        spinner.SetSizeRequest(32, 32);
        spinner.Halign = spinner.Valign = Gtk.Align.Center;

        _listStack = Gtk.Stack.New();
        _listStack.AddNamed(_scroller, "list");
        _listStack.AddNamed(_emptyStatus, "empty");
        _listStack.AddNamed(spinner, "loading");
        _listStack.VisibleChildName = "loading";
        return _listStack;
    }

    private MailRowView? SlotOf(Gtk.ListItem item)
    {
        var name = item.Child?.Name;
        if (name is null || !name.StartsWith("mail-slot-", StringComparison.Ordinal)) return null;
        return int.TryParse(name.AsSpan("mail-slot-".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index < _slots.Count ? _slots[index] : null;
    }

    private void BindList()
    {
        if (_listBound) return;
        _listBound = true;
        _projection = new MailListProjection(ViewModel.MailCollection.Items, ViewModel.MailListOptions ?? new MailListProjectionOptions());
        _projection.ProjectionChanged += ProjectionChanged;
        ApplyEntries();

        Form.Bind(nameof(ViewModel.MailListOptions), vm => vm.MailListOptions, options => _projection?.SetOptions(options ?? new MailListProjectionOptions()));
        Form.Bind(nameof(ViewModel.IsInitializingFolder), vm => vm.IsInitializingFolder, _ => UpdateOverlays());
        Form.Bind(nameof(ViewModel.IsEmpty), vm => vm.IsEmpty, _ => UpdateOverlays());
        Form.Bind(nameof(ViewModel.IsInSearchMode), vm => vm.IsInSearchMode, _ => UpdateOverlays());
        _preferences.PreferenceChanged += PreferenceChanged;
        _pictures.PictureChanged += SenderPictureChanged;
        RegisterListDebugCommands();
    }

    private void ReleaseList()
    {
        if (!_listBound) return;
        _listBound = false;
        _preferences.PreferenceChanged -= PreferenceChanged;
        _pictures.PictureChanged -= SenderPictureChanged;
        if (_projection is not null)
        {
            _projection.ProjectionChanged -= ProjectionChanged;
            _projection.Dispose();
            _projection = null;
        }
        foreach (var slot in _slots) slot.Subscription = null;
        _entries.Clear();
        _entryByKey.Clear();
    }

    private void PreferenceChanged(object? sender, string name) => OnUI(() =>
    {
        if (name is nameof(IPreferencesService.MailItemDisplayMode) or nameof(IPreferencesService.IsShowPreviewEnabled)
            or nameof(IPreferencesService.IsShowSenderPicturesEnabled) or nameof(IPreferencesService.MailTimeFormatPreference))
            RefreshVisibleSlots(null);
    });

    /// <summary>A sender picture arrived or changed: re-apply the bound rows from that sender (all rows when null).</summary>
    private void SenderPictureChanged(object? sender, string? address) => OnUI(() => RefreshVisibleSlots(address));

    private void RefreshVisibleSlots(string? address)
    {
        if (!_listBound) return;
        foreach (var slot in _slots)
        {
            if (!_entryByKey.TryGetValue(slot.Key, out var entry) || entry.Row is not { } row) continue;
            var model = MapRow(row);
            if (address is not null && !string.Equals(model.Content.SenderAddress.Trim(), address, StringComparison.OrdinalIgnoreCase)) continue;
            slot.ShowRow(model);
        }
    }

    // ---- Projection to list ----

    /// <summary>
    /// Coalesces projection changes: synchronization publishes many small batches, and each would otherwise splice the
    /// list. The first change applies on the next main-loop turn, later ones in a burst wait for the throttle window.
    /// The projection may raise from any thread.
    /// </summary>
    private void ProjectionChanged(object? sender, EventArgs args) => OnUI(() =>
    {
        if (_projectionApplyScheduled) return;
        _projectionApplyScheduled = true;
        GLib.Functions.TimeoutAdd(DefaultPriority, ProjectionThrottleMilliseconds, () =>
        {
            _projectionApplyScheduled = false;
            if (!_released && _listBound)
            {
                try { ApplyEntries(); }
                catch (Exception exception) { ReportError(exception); }
            }
            return false;
        });
    });

    private void RebuildEntries()
    {
        _entries.Clear();
        _entryByKey.Clear();
        if (_projection is null) return;
        bool grouped = (ViewModel.MailListOptions?.GroupMode ?? MailListGroupMode.Date) != MailListGroupMode.None;
        foreach (var group in _projection.Groups)
        {
            if (group.Count == 0) continue;
            if (grouped)
            {
                var title = MailRowPresentation.GroupTitle(group.Key);
                if (!string.IsNullOrEmpty(title)) Add(new ListEntry("G:" + title, null, title));
            }
            foreach (var row in group) Add(new ListEntry(SelectionKey(row), row, string.Empty));
        }

        void Add(ListEntry entry)
        {
            // Keys identify list items; a duplicate (the same group title twice) would make two slots one item.
            if (_entryByKey.TryAdd(entry.Key, entry)) _entries.Add(entry);
        }
    }

    private void ApplyEntries()
    {
        if (_projection is null) return;
        var previousKeys = _entries.Select(static entry => entry.Key).ToArray();
        int previousFirst = FirstSelectedPosition();
        int previousRowCount = _lastRowCount;
        int previousSelectionCount = _selectionKeys.Count;
        RebuildEntries();
        _lastRowCount = _entries.Count(static entry => entry.Row is not null);

        _restoringSelection = true;
        try
        {
            Splice(previousKeys, _entries.Select(static entry => entry.Key).ToArray());
            var positions = new List<uint>();
            for (int index = 0; index < _entries.Count; index++)
                if (_entries[index].Row is not null && _selectionKeys.Contains(_entries[index].Key)) positions.Add((uint)index);

            // Removing the shown message selects its neighbour when the preference asks for it.
            bool smallRemoval = previousRowCount - _lastRowCount is > 0 && previousRowCount - _lastRowCount <= previousSelectionCount + 1;
            if (positions.Count == 0 && previousSelectionCount > 0 && previousFirst >= 0 && smallRemoval
                && _preferences.AutoSelectNextItem && !ViewModel.IsInitializingFolder && !ViewModel.IsMultiSelectionModeEnabled)
            {
                int candidate = Math.Min(previousFirst, _entries.Count - 1);
                while (candidate >= 0 && candidate < _entries.Count && _entries[candidate].Row is null) candidate++;
                if (candidate >= _entries.Count) candidate = _entries.FindLastIndex(static entry => entry.Row is not null);
                if (candidate >= 0) positions.Add((uint)candidate);
            }
            SetSelectedPositions(positions);
        }
        finally { _restoringSelection = false; }

        SyncSelection();
        UpdateOverlays();
        CheckLoadMore();
    }

    /// <summary>Replaces only the changed middle of the key list: the common prefix and suffix keep their items.</summary>
    private void Splice(string[] previous, string[] next)
    {
        int prefix = 0;
        while (prefix < previous.Length && prefix < next.Length && previous[prefix] == next[prefix]) prefix++;
        int suffix = 0;
        while (suffix < previous.Length - prefix && suffix < next.Length - prefix
            && previous[previous.Length - 1 - suffix] == next[next.Length - 1 - suffix]) suffix++;
        int removed = previous.Length - prefix - suffix;
        var added = next[prefix..(next.Length - suffix)];
        if (removed > 0 || added.Length > 0) _keys.Splice((uint)prefix, (uint)removed, added);
        // Thread heads summarise leaves that may have changed inside an unchanged entry list.
        if (removed == 0 && added.Length == 0) RefreshThreadHeads();
    }

    private void RefreshThreadHeads()
    {
        foreach (var slot in _slots)
            if (_entryByKey.TryGetValue(slot.Key, out var entry) && entry.Row is { IsThreadHead: true } row) slot.ShowRow(MapRow(row));
    }

    private void UpdateOverlays()
    {
        bool empty = _entries.Count == 0;
        if (!empty) _listStack.VisibleChildName = "list";
        else if (ViewModel.IsInitializingFolder) _listStack.VisibleChildName = "loading";
        else
        {
            _emptyStatus.Title = ViewModel.IsInSearchMode ? Translator.NoMessageCrieteria : Translator.NoMessageEmptyFolder;
            _listStack.VisibleChildName = "empty";
        }
    }

    private void CheckLoadMore()
    {
        if (!_listBound || _entries.Count == 0) return;
        var adjustment = _scroller.GetVadjustment();
        if (adjustment.Upper - (adjustment.Value + adjustment.PageSize) > LoadMoreDistance) return;
        var command = ViewModel.LoadMoreItemsCommand;
        if (command.CanExecute(null)) Observe(command.ExecuteAsync(null));
    }

    // ---- Slots ----

    private MailRowModel MapRow(MailListRow row)
    {
        var content = MailRowPresentation.Describe(row, _preferences.MailTimeFormatPreference)
            ?? throw new InvalidOperationException("A mail list row must stand for a mail item.");
        bool showPicture = _preferences.IsShowSenderPicturesEnabled;
        return new MailRowModel(content, MailRowPresentation.Density(_preferences.MailItemDisplayMode), showPicture,
            _preferences.IsShowPreviewEnabled, showPicture ? _pictures.Get(content.SenderAddress) : null);
    }

    private void BindSlot(Gtk.ListItem item, MailRowView slot, string key)
    {
        slot.Subscription = null;
        slot.Key = key;
        if (!_entryByKey.TryGetValue(key, out var entry)) return;
        if (entry.Row is not { } row)
        {
            item.Selectable = false;
            item.Activatable = false;
            slot.ShowHeader(entry.Title);
            return;
        }
        item.Selectable = true;
        item.Activatable = true;
        slot.ShowRow(MapRow(row));

        // A synchronized mail raises many property changes in a row (and a thread head hears all of its leaves):
        // they coalesce into one re-map per main-loop turn.
        var leaves = (row.IsThreadHead ? row.LeafItems : [row.SourceItem]).OfType<INotifyPropertyChanged>().ToArray();
        bool pending = false;
        bool active = true;
        void Schedule() => OnUI(() =>
        {
            if (pending) return;
            pending = true;
            GLib.Functions.IdleAdd(DefaultIdlePriority, () =>
            {
                pending = false;
                if (active && !_released && slot.Key == key && _entryByKey.ContainsKey(key)) slot.ShowRow(MapRow(row));
                return false;
            });
        });
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(MailItemViewModel.IsSelected) or nameof(MailItemViewModel.ThumbnailUpdatedEvent)) return;
            Schedule();
        };
        foreach (var leaf in leaves) leaf.PropertyChanged += handler;
        row.PropertyChanged += handler;
        slot.Subscription = new Presentation.ActionDisposable(() =>
        {
            active = false;
            foreach (var leaf in leaves) leaf.PropertyChanged -= handler;
            row.PropertyChanged -= handler;
        });
    }

    // ---- Selection ----

    private static string SelectionKey(MailListRow row)
        => row.IsThreadHead ? "T:" + row.ThreadKey : "I:" + row.SourceItem.StableId.ToString("N");

    private List<int> SelectedPositions()
    {
        var positions = new List<int>();
        var bitset = _selection.GetSelection();
        var size = bitset.GetSize();
        for (uint nth = 0; nth < size; nth++) positions.Add((int)bitset.GetNth(nth));
        return positions;
    }

    private int FirstSelectedPosition()
    {
        var positions = SelectedPositions();
        return positions.Count > 0 ? positions.Min() : -1;
    }

    private void SetSelectedPositions(IReadOnlyCollection<uint> positions)
    {
        _selection.UnselectAll();
        foreach (var position in positions) _selection.SelectItem(position, false);
    }

    /// <summary>
    /// Windows thread click: selecting a collapsed thread expands it and selects its representative message (newest or
    /// oldest first, per the thread order preference). Otherwise the selection goes to the ViewModel.
    /// </summary>
    private void SelectionChanged()
    {
        var positions = SelectedPositions();
        if (positions.Count == 1 && positions[0] < _entries.Count
            && _entries[positions[0]].Row is { IsThreadHead: true, IsExpanded: false, Thread: { Count: > 1 } thread } && _projection is not null)
        {
            _selectionKeys.Clear();
            _selectionKeys.Add("I:" + thread.RepresentativeItem.StableId.ToString("N"));
            _projection.ExpandThread(thread.Key);
            ApplyEntries();
            return;
        }
        SyncSelection();
    }

    private void ToggleThread(MailListRow row)
    {
        if (_projection is null || row.Thread is null) return;
        if (row.IsExpanded) _projection.CollapseThread(row.Thread.Key);
        else _projection.ExpandThread(row.Thread.Key);
    }

    private void SyncSelection()
    {
        if (_projection is null) return;
        var positions = SelectedPositions();
        var selectedRows = new List<MailListRow>();
        foreach (var position in positions)
            if (position < _entries.Count && _entries[position].Row is { } row) selectedRows.Add(row);

        _selectionKeys.Clear();
        foreach (var row in selectedRows) _selectionKeys.Add(SelectionKey(row));

        var focused = positions.Count > 0 && positions[0] < _entries.Count ? _entries[positions.Min()].Row : null;
        var result = MailListSelectionSnapshotBuilder.Build(selectedRows, _projection, focused);
        if (result.Signature == _lastPublishedSelection) return;
        _lastPublishedSelection = result.Signature;
        ViewModel.ApplyMailSelectionSnapshot(result.Snapshot);
    }

    /// <summary>
    /// Debug bridge commands: <c>mail-state</c> (rows, selection and the first rows' text) and <c>mail-select N...</c>
    /// (selects mail rows by their position among the mail rows, group headers not counted).
    /// </summary>
    private void RegisterListDebugCommands()
    {
#if DEBUG
        int MailEntryIndex(int ordinal)
        {
            int seen = -1;
            for (int index = 0; index < _entries.Count; index++)
                if (_entries[index].Row is not null && ++seen == ordinal) return index;
            return -1;
        }
        LinuxDebugBridge.Register("mail-state", _ =>
        {
            if (_released) return Task.FromResult("released");
            var lines = _entries.Take(12).Select(entry => entry.Row is { } row && MailRowPresentation.Describe(row) is { } content
                ? $"{(content.IsUnread ? "*" : string.Empty)}{content.Sender}|{content.Subject}|{content.DateText}{(content.IsFlagged ? "|flag" : string.Empty)}{(content.HasAttachments ? "|att" : string.Empty)}"
                : $"[{entry.Title}]");
            return Task.FromResult($"rows={_lastRowCount} entries={_entries.Count} selected={ViewModel.SelectedItemsCount} folder={ViewModel.ActiveFolder?.FolderName} :: " + string.Join(" ; ", lines));
        });
        LinuxDebugBridge.Register("mail-select", args =>
        {
            if (_released) return Task.FromResult("released");
            var positions = new List<uint>();
            foreach (var arg in args.Length == 0 ? ["0"] : args)
            {
                int index = int.TryParse(arg, out var ordinal) ? MailEntryIndex(ordinal) : -1;
                if (index >= 0) positions.Add((uint)index);
            }
            if (positions.Count == 0) return Task.FromResult("no such row");
            SetSelectedPositions(positions);
            _listView.ScrollTo(positions[0], Gtk.ListScrollFlags.None, null);
            return Task.FromResult($"ok selected={ViewModel.SelectedItemsCount}");
        });
#endif
    }
}
