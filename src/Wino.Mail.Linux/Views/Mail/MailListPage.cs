using Wino.Core.Domain;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.Linux.Views.Shell;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The Mail mode's content page: list and reader in an <c>AdwNavigationSplitView</c> (list 380 px, reader fills), each
/// with its own header bar; below 641 px they become a drill-in. Milestone 2c builds the frame only: the list pane
/// names the folder and the reader shows its empty state. Milestone 2d binds the list to the shared
/// <c>MailListPageViewModel</c>. The folder route waits for the page to report the folder loaded, so the page completes
/// <see cref="NavigateMailFolderEventArgs.FolderInitLoadAwaitTask"/> as the real list does once its first page loads.
/// </summary>
public sealed class MailListPage(LinuxNavigationService navigation, ShellWindow window, IDispatcher dispatcher) : ILinuxPage
{
    private const int ListWidth = 380;
    private readonly Adw.NavigationSplitView _split = Adw.NavigationSplitView.New();
    private readonly Adw.WindowTitle _listTitle = Adw.WindowTitle.New(string.Empty, string.Empty);
    private readonly Adw.StatusPage _listStatus = Adw.StatusPage.New();
    private readonly Adw.HeaderBar _listHeader = Adw.HeaderBar.New();
    private WinoAppShellPage? _shell;
    private Gtk.Widget? _widget;

    public Gtk.Widget Widget => _widget ??= Build();
    public bool HasPendingWork => false;
    public object AssociatedViewModel => this;

    private Gtk.Widget Build()
    {
        // The sidebar toggle is window chrome, so it is GTK's stock icon (design decisions, Icons).
        var sidebarToggle = Gtk.Button.NewFromIconName("sidebar-show-symbolic");
        sidebarToggle.TooltipText = Translator.MacOSMenu_ToggleSidebar;
        sidebarToggle.OnClicked += (_, _) => _shell?.ToggleSidebar();
        _listHeader.TitleWidget = _listTitle;
        _listHeader.PackStart(sidebarToggle);
        _listStatus.IconName = "mail-unread-symbolic";
        _listStatus.Description = Translator.ComingSoon;
        _listStatus.Vexpand = true;
        var list = Adw.ToolbarView.New();
        list.AddTopBar(_listHeader);
        list.Content = _listStatus;

        var reader = Adw.ToolbarView.New();
        var readerHeader = Adw.HeaderBar.New();
        readerHeader.ShowTitle = false;
        reader.AddTopBar(readerHeader);
        var empty = Adw.StatusPage.New();
        empty.Child = WinoIcons.Label(Wino.Core.Domain.Enums.WinoIconGlyph.Mail, 20);
        empty.Title = Translator.NoMailSelected;
        empty.AddCssClass("dim-label");
        reader.Content = empty;

        _split.Sidebar = Adw.NavigationPage.New(list, Translator.KeyboardShortcuts_ModeMail);
        _split.Content = Adw.NavigationPage.New(reader, Translator.NoMailSelected);
        _split.MinSidebarWidth = _split.MaxSidebarWidth = ListWidth;
        window.SizeClassChanged += SizeClassChanged;
        ApplySizeClass(window.SizeClass);
        return _split;
    }

    public async Task ActivateAsync(NavigationMode mode, object? parameter)
    {
        var folder = parameter as NavigateMailFolderEventArgs;
        await dispatcher.ExecuteOnUIThread(() =>
        {
            _ = Widget;
            _shell = navigation.Shell;
            if (_shell is not null) _shell.SidebarDockedChanged += SidebarDockedChanged;
            ApplyListTitle();
            var name = folder?.BaseFolderMenuItem?.FolderName ?? string.Empty;
            _listTitle.Title = name;
            _listStatus.Title = name;
            _split.ShowContent = false;
        });
        folder?.FolderInitLoadAwaitTask?.TrySetResult(true);
    }

    public Task ReleaseAsync() => dispatcher.ExecuteOnUIThread(() =>
    {
        window.SizeClassChanged -= SizeClassChanged;
        if (_shell is not null) _shell.SidebarDockedChanged -= SidebarDockedChanged;
        _shell = null;
    });

    private void SidebarDockedChanged(object? sender, EventArgs args) => ApplyListTitle();

    /// <summary>The docked sidebar's header already names the folder; otherwise the list header does.</summary>
    private void ApplyListTitle() => _listHeader.ShowTitle = _shell?.IsSidebarDocked != true;

    private void SizeClassChanged(object? sender, WinoSizeClass sizeClass) => ApplySizeClass(sizeClass);

    private void ApplySizeClass(WinoSizeClass sizeClass) => _split.Collapsed = sizeClass == WinoSizeClass.Small;
}
