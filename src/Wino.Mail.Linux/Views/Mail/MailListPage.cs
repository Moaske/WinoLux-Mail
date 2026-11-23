using CommunityToolkit.Mvvm.Messaging;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.Linux.Views.Shell;
using Wino.Mail.ViewModels;
using Wino.Mail.ViewModels.Messages;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The Mail mode's content page over the shared <see cref="MailListPageViewModel"/>: list and reader in an
/// <c>AdwNavigationSplitView</c> (list 380 px, reader fills), each with its own header bar; below 641 px they become a
/// drill-in. The list is <see cref="MailListPage"/>'s List part; the reader shows its empty state until milestone 2e.
/// Activation follows the Mac MailListPageViewController: bind the list, pass the folder to the ViewModel and wait for
/// its first load, which completes the folder route.
/// </summary>
public sealed partial class MailListPage : LinuxPage<MailListPageViewModel>
{
    private const int ListWidth = 380;
    private readonly LinuxNavigationService _navigation;
    private readonly ShellWindow _window;
    private readonly LinuxSenderPictureProvider _pictures;
    private readonly IPreferencesService _preferences;
    private readonly Adw.NavigationSplitView _split = Adw.NavigationSplitView.New();
    private readonly Adw.WindowTitle _listTitle = Adw.WindowTitle.New(string.Empty, string.Empty);
    private readonly Adw.HeaderBar _listHeader = Adw.HeaderBar.New();
    private WinoAppShellPage? _shell;
    private bool _released;

    public MailListPage(MailListPageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger,
        LinuxNavigationService navigation, ShellWindow window, LinuxSenderPictureProvider pictures) : base(viewModel, dispatcher, logger)
    {
        _navigation = navigation;
        _window = window;
        _pictures = pictures;
        _preferences = viewModel.PreferencesService;
        // The store raises its changes through this dispatcher, so the projection sees them on the UI thread.
        ViewModel.MailCollection.CoreDispatcher = dispatcher;
    }

    protected override Gtk.Widget BuildView()
    {
        // The sidebar toggle is window chrome, so it is GTK's stock icon (design decisions, Icons).
        var sidebarToggle = Gtk.Button.NewFromIconName("sidebar-show-symbolic");
        sidebarToggle.TooltipText = Translator.MacOSMenu_ToggleSidebar;
        sidebarToggle.OnClicked += (_, _) => _shell?.ToggleSidebar();
        _listHeader.TitleWidget = _listTitle;
        _listHeader.PackStart(sidebarToggle);
        var list = Adw.ToolbarView.New();
        list.AddTopBar(_listHeader);
        list.Content = BuildListPane();

        var reader = Adw.ToolbarView.New();
        var readerHeader = Adw.HeaderBar.New();
        readerHeader.ShowTitle = false;
        reader.AddTopBar(readerHeader);
        var empty = Adw.StatusPage.New();
        empty.Child = WinoIcons.Label(WinoIconGlyph.Mail, 20);
        empty.Title = Translator.NoMailSelected;
        empty.AddCssClass("dim-label");
        reader.Content = empty;

        _split.Sidebar = Adw.NavigationPage.New(list, Translator.KeyboardShortcuts_ModeMail);
        _split.Content = Adw.NavigationPage.New(reader, Translator.NoMailSelected);
        _split.MinSidebarWidth = _split.MaxSidebarWidth = ListWidth;
        _window.SizeClassChanged += SizeClassChanged;
        ApplySizeClass(_window.SizeClass);
        return _split;
    }

    protected override async Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        _shell = _navigation.Shell;
        if (_shell is not null) _shell.SidebarDockedChanged += SidebarDockedChanged;
        ApplyListTitle();
        Form.Bind(nameof(ViewModel.ActiveFolder), vm => vm.ActiveFolder?.FolderName ?? string.Empty, name => _listTitle.Title = name);
        _split.ShowContent = false;

        BindList();
        ViewModel.OnNavigatedTo(mode, parameter!);
        if (parameter is NavigateMailFolderEventArgs folder)
        {
            WeakReferenceMessenger.Default.Send(new ActiveMailFolderChangedEvent(folder.BaseFolderMenuItem, folder.FolderInitLoadAwaitTask));
            await ViewModel.WaitForCurrentFolderInitializationAsync();
        }
    }

    protected override async Task DeactivateAsync()
    {
        _released = true;
        _window.SizeClassChanged -= SizeClassChanged;
        if (_shell is not null) _shell.SidebarDockedChanged -= SidebarDockedChanged;
        _shell = null;
        ReleaseList();
        await ViewModel.DeactivateAsync(NavigationMode.New, null);
    }

    private void SidebarDockedChanged(object? sender, EventArgs args) => ApplyListTitle();

    /// <summary>The docked sidebar's header already names the folder; otherwise the list header does.</summary>
    private void ApplyListTitle() => _listHeader.ShowTitle = _shell?.IsSidebarDocked != true;

    private void SizeClassChanged(object? sender, WinoSizeClass sizeClass) => ApplySizeClass(sizeClass);

    private void ApplySizeClass(WinoSizeClass sizeClass) => _split.Collapsed = sizeClass == WinoSizeClass.Small;

    private void OnUI(Action action)
    {
        if (_released) return;
        _ = Dispatcher.ExecuteOnUIThread(() => { if (!_released) action(); });
    }

    private async void Observe(Task task)
    {
        try { await task; }
        catch (Exception exception) { ReportError(exception); }
    }
}
