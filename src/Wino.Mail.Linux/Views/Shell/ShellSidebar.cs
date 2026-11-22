using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels.Data;
using Wino.Presentation.Controls;

namespace Wino.Mail.Linux.Views.Shell;

/// <summary>
/// The shell pane's menu as a GNOME navigation sidebar: New item, accounts with their unread pill, section headers,
/// folders by depth with their special-folder glyphs, More, and the footer rows. Row content comes from the shared
/// <see cref="ShellMenuRowPresentation"/> (the Windows pane templates), as on Android. It follows every shown item and
/// the menu collections, rebuilt once per main-loop turn, and shows the shell's selected item as the row selection.
/// The shell owns the ViewModel; the sidebar only presents and forwards activations.
/// </summary>
internal sealed partial class ShellSidebar : IDisposable
{
    private readonly Gtk.ListBox _list = Gtk.ListBox.New();
    private readonly IPictureStorageService? _pictures;
    private readonly IDispatcher _dispatcher;
    private readonly Action<Exception> _error;
    private readonly List<(IMenuItem Item, Gtk.ListBoxRow Row)> _rows = [];
    private readonly List<INotifyPropertyChanged> _observedItems = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];
    private ShellMenu? _menu;
    private IMenuItem? _selected;
    private bool _rebuildScheduled;
    private bool _applyingSelection;
    private bool _disposed;

    public ShellSidebar(IPictureStorageService? pictures, IDispatcher dispatcher, Action<Exception> error)
    {
        _pictures = pictures;
        _dispatcher = dispatcher;
        _error = error;
        _list.AddCssClass("navigation-sidebar");
        _list.SelectionMode = Gtk.SelectionMode.Single;
        _list.OnRowActivated += (_, args) =>
        {
            if (Find(args.Row) is { } item) Invoked?.Invoke(this, item);
        };
        _list.OnRowSelected += (_, args) =>
        {
            // GTK selects a row on click before it activates it; the shell's selection decides what stays selected.
            if (_applyingSelection || args.Row is null) return;
            Defer(ApplySelection);
        };
        Widget = Gtk.ScrolledWindow.New();
        Widget.HscrollbarPolicy = Gtk.PolicyType.Never;
        Widget.Vexpand = true;
        Widget.Child = _list;
    }

    public Gtk.ScrolledWindow Widget { get; }

    /// <summary>A row was clicked or activated with the keyboard.</summary>
    public event EventHandler<IMenuItem>? Invoked;

    public void Bind(ShellMenu? menu)
    {
        _menu = menu;
        Rebuild();
    }

    public void SetSelectedItem(IMenuItem? item)
    {
        _selected = item;
        ApplySelection();
    }

    /// <summary>The visible rows as "kind:title" lines, for the debug bridge.</summary>
    public string Describe()
        => string.Join(" | ", _rows.Select(entry =>
        {
            var row = ShellMenuRowPresentation.Describe(entry.Item, 0, null);
            var mark = ReferenceEquals(entry.Item, _selected) ? "*" : string.Empty;
            return $"{mark}{row.Kind}:{row.Title.Split('\n')[0]}{(row.UnreadCount > 0 ? $"({row.UnreadCount})" : string.Empty)}";
        }));

    /// <summary>The menu items currently shown, for the debug bridge.</summary>
    public IEnumerable<IMenuItem> Items => _rows.Select(entry => entry.Item);

    private void ScheduleRebuild()
    {
        // Menu items change on any thread; the flag is only touched on the UI thread.
        _ = _dispatcher.ExecuteOnUIThread(() =>
        {
            if (_rebuildScheduled || _disposed) return;
            _rebuildScheduled = true;
            Defer(() =>
            {
                _rebuildScheduled = false;
                Rebuild();
            });
        });
    }

    /// <summary>Runs <paramref name="action"/> on a later main-loop turn. UI thread.</summary>
    private void Defer(Action action)
    {
        GLib.Functions.IdleAdd(DefaultIdlePriority, () =>
        {
            if (!_disposed)
            {
                try { action(); }
                catch (Exception exception) { _error(exception); }
            }
            return false;
        });
    }

    private const int DefaultIdlePriority = 200;

    private void Rebuild()
    {
        try
        {
            Unobserve();
            _rows.Clear();
            _list.RemoveAll();
            if (_menu is not { } menu) return;
            var entries = ShellMenuRowPresentation.Flatten(menu.Items);
            Observe(menu.Items);
            if (menu.FooterItems is { } footer)
            {
                entries.AddRange(ShellMenuRowPresentation.Flatten(footer));
                Observe(footer);
            }
            foreach (var (item, depth) in entries)
            {
                var row = CreateRow(item, ShellMenuRowPresentation.Describe(item, depth, _pictures));
                _rows.Add((item, row));
                _list.Append(row);
                if (item is INotifyPropertyChanged notifying)
                {
                    notifying.PropertyChanged += ItemChanged;
                    _observedItems.Add(notifying);
                }
                Observe(ShellMenuItemRules.Children(item));
            }
            ApplySelection();
        }
        catch (Exception exception) { _error(exception); }
    }

    private Gtk.ListBoxRow CreateRow(IMenuItem item, ShellMenuRowContent content)
    {
        var row = Gtk.ListBoxRow.New();
        row.Sensitive = content.IsEnabled;
        row.Selectable = content.IsHighlightable && ShellMenuItemRules.SelectsOnInvoked(item);
        row.Activatable = ShellMenuItemRules.IsInteractive(item);
        var firstLine = content.Title.Split('\n')[0].Trim();

        if (content.Kind == ShellMenuRowKind.Header)
        {
            var header = GtkUi.Label(firstLine, "wino-sidebar-header");
            header.MarginStart = 6;
            row.Child = header;
            row.Selectable = row.Activatable = false;
            return row;
        }

        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        box.MarginStart = 4 + content.Indent * 14;
        box.MarginEnd = 2;
        row.AddCssClass(content.Kind == ShellMenuRowKind.Account ? "wino-account-row" : "wino-sidebar-row");

        if (content.Kind == ShellMenuRowKind.Account) box.Append(AccountIcon(content));
        else box.Append(WinoIcons.Label(content.Glyph, 16));

        var title = Gtk.Label.New(firstLine);
        title.Xalign = 0;
        title.Hexpand = true;
        title.Ellipsize = Pango.EllipsizeMode.End;
        if (content.Kind is ShellMenuRowKind.Account or ShellMenuRowKind.NewItem) title.AddCssClass("heading");
        box.Append(title);
        row.TooltipText = string.IsNullOrWhiteSpace(content.Subtitle) ? content.Title : $"{firstLine}\n{content.Subtitle}";

        if (content.IsAttentionRequired) box.Append(WinoIcons.Label(Wino.Core.Domain.Enums.WinoIconGlyph.Warning, 16));
        if (content.ShowsSync)
        {
            var spinner = Adw.Spinner.New();
            spinner.WidthRequest = spinner.HeightRequest = 14;
            box.Append(spinner);
        }
        if (content.UnreadCount > 0)
        {
            var pill = GtkUi.Label(content.UnreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture), "wino-pill");
            pill.Valign = Gtk.Align.Center;
            box.Append(pill);
        }
        if (content.IsExpanded is { } expanded)
        {
            var chevron = WinoIcons.Button(expanded ? Wino.Core.Domain.Enums.WinoIconGlyph.ChevronDown : Wino.Core.Domain.Enums.WinoIconGlyph.ChevronRight,
                firstLine, 14);
            chevron.Valign = Gtk.Align.Center;
            chevron.OnClicked += (_, _) => item.IsExpanded = !item.IsExpanded;
            box.Append(chevron);
        }
        row.Child = box;
        return row;
    }

    /// <summary>The provider glyph tinted with the account colour (Windows WinoAccountIcon without a picture).</summary>
    private static Gtk.Widget AccountIcon(ShellMenuRowContent content)
    {
        var icon = content.AccountIcon;
        var glyph = WinoIcons.Label(icon is null ? Wino.Core.Domain.Enums.WinoIconGlyph.IMAP : WinoAccountIconGlyphs.ProviderGlyph(icon.Provider, true), 20);
        if (icon?.AccountColorHex is { } hex && HexColor().IsMatch(hex))
        {
            glyph.UseMarkup = true;
            glyph.SetLabel($"<span foreground=\"{hex}\">{GLib.Functions.MarkupEscapeText(glyph.GetText(), -1)}</span>");
        }
        return glyph;
    }

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();

    private void ApplySelection()
    {
        if (_disposed) return;
        _applyingSelection = true;
        try
        {
            var match = _selected is null ? null : _rows.FirstOrDefault(entry => ReferenceEquals(entry.Item, _selected)).Row;
            if (match is not null && match.Selectable) _list.SelectRow(match);
            else _list.UnselectAll();
        }
        finally { _applyingSelection = false; }
    }

    private IMenuItem? Find(Gtk.ListBoxRow row)
    {
        var handle = row.Handle.DangerousGetHandle();
        foreach (var (item, candidate) in _rows)
            if (candidate.Handle.DangerousGetHandle() == handle) return item;
        return null;
    }

    private void Observe(IEnumerable<IMenuItem> collection)
    {
        if (collection is not INotifyCollectionChanged notifying) return;
        notifying.CollectionChanged += CollectionChanged;
        _observedCollections.Add(notifying);
    }

    private void Unobserve()
    {
        foreach (var item in _observedItems) item.PropertyChanged -= ItemChanged;
        foreach (var collection in _observedCollections) collection.CollectionChanged -= CollectionChanged;
        _observedItems.Clear();
        _observedCollections.Clear();
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Selection is the shell's to apply; every other change redraws the rows.
        if (args.PropertyName == nameof(IMenuItem.IsSelected)) return;
        ScheduleRebuild();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => ScheduleRebuild();

    public void Dispose()
    {
        _disposed = true;
        Unobserve();
        Invoked = null;
    }
}
