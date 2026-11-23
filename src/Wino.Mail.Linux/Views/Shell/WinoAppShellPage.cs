using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels.Data;
using Wino.Presentation.Bindings;
using Wino.Presentation.Controls;
using Wino.Shell.ViewModels;

namespace Wino.Mail.Linux.Views.Shell;

/// <summary>
/// The main window's shell (the Mac WinoAppShellViewController, design decisions Control mapping): an
/// <c>AdwOverlaySplitView</c> with the Wino pane (sync button with the folder title, the main menu, the menu of the
/// current mode, the mode switcher at its foot) beside the mode's content page. Modes activate through the shared
/// <see cref="ShellModeActivator"/>: the provider publishes its menu and navigates its root page into the content
/// area. The pane becomes an overlay below 1008 px; the content page's header bar carries the sidebar toggle.
/// </summary>
public sealed partial class WinoAppShellPage : LinuxPage<WinoAppShellViewModel>
{
    private const int SidebarWidth = 260;
    private readonly LinuxNavigationService _navigation;
    private readonly IStatePersistanceService _state;
    private readonly IServiceProvider _services;
    private readonly ShellWindow _window;
    private readonly ShellModeActivator _modes;
    private readonly Adw.OverlaySplitView _split = Adw.OverlaySplitView.New();
    private readonly Gtk.Box _contentHost = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
    private readonly Adw.WindowTitle _title = Adw.WindowTitle.New(string.Empty, string.Empty);
    private readonly Adw.ToggleGroup _modeSwitcher = Adw.ToggleGroup.New();
    private Gtk.Button? _sync;
    private Adw.Spinner? _syncSpinner;
    private ShellSidebar? _sidebar;
    private ILinuxPage? _child;
    private IShellMenuProvider? _provider;
    private INotifyPropertyChanged? _titleSource;
    private bool _applyingMode;

    public WinoAppShellPage(WinoAppShellViewModel viewModel, LinuxNavigationService navigation, IStatePersistanceService state,
        IServiceProvider services, ShellWindow window, IDispatcher dispatcher, IWinoLogger logger) : base(viewModel, dispatcher, logger)
    {
        _navigation = navigation;
        _state = state;
        _services = services;
        _window = window;
        _modes = new ShellModeActivator(viewModel, state, ReportError);
    }

    /// <summary>The page in the content area, for the navigation host.</summary>
    public ILinuxPage? Content => _child;

    /// <summary>True while the pane sits beside the content (wide layout, open), where its header names the folder.</summary>
    public bool IsSidebarDocked => _split.ShowSidebar && !_split.Collapsed;

    /// <summary>Raised on the UI thread when <see cref="IsSidebarDocked"/> may have changed.</summary>
    public event EventHandler? SidebarDockedChanged;

    /// <summary>Opens or closes the pane: docked in the wide layout, as an overlay below 1008 px.</summary>
    public void ToggleSidebar() => _split.ShowSidebar = !_split.ShowSidebar;

    protected override Gtk.Widget BuildView()
    {
        _sidebar = new ShellSidebar(_services.GetService<IPictureStorageService>(), Dispatcher, ReportError);
        _sidebar.Invoked += (_, item) => Select(item);

        var header = Adw.HeaderBar.New();
        header.TitleWidget = _title;
        _sync = Gtk.Button.New();
        _sync.AddCssClass("flat");
        _syncSpinner = Adw.Spinner.New();
        _syncSpinner.Visible = false;
        var syncContent = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        syncContent.Append(WinoIcons.Label(WinoIconGlyph.Sync, 16));
        syncContent.Append(_syncSpinner);
        _sync.Child = syncContent;
        _sync.TooltipText = Translator.Buttons_Sync;
        _sync.OnClicked += (_, _) => Synchronize();
        header.PackStart(_sync);
        header.PackEnd(MainMenu());

        var pane = Adw.ToolbarView.New();
        pane.AddTopBar(header);
        pane.Content = _sidebar.Widget;
        pane.AddBottomBar(BuildModeSwitcher());

        _contentHost.Hexpand = _contentHost.Vexpand = true;
        _split.Sidebar = pane;
        _split.Content = _contentHost;
        _split.MinSidebarWidth = _split.MaxSidebarWidth = SidebarWidth;
        _split.ShowSidebar = ViewModel.PreferencesService.IsNavigationPaneOpened;
        _split.OnNotify += (_, args) =>
        {
            var name = args.Pspec.GetName();
            if (name is not ("show-sidebar" or "collapsed")) return;
            // The pane's open state is a preference only in the wide layout; the overlay closes after each pick.
            if (name == "show-sidebar" && !_split.Collapsed)
                ViewModel.PreferencesService.IsNavigationPaneOpened = _split.ShowSidebar;
            SidebarDockedChanged?.Invoke(this, EventArgs.Empty);
        };
        ApplySizeClass(_window.SizeClass);
        _window.SizeClassChanged += SizeClassChanged;

        Bindings.Own(new PropertyBinding<WinoAppShellViewModel, ShellMenu?>(ViewModel, nameof(ViewModel.CurrentMenu),
            vm => vm.CurrentMenu, menu => _sidebar.Bind(menu), Dispatcher, ReportError));
        Bindings.Own(new PropertyBinding<WinoAppShellViewModel, object?>(ViewModel, nameof(ViewModel.SelectedMenuItem),
            vm => vm.SelectedMenuItem, item =>
            {
                _sidebar.SetSelectedItem(item as IMenuItem);
                ObserveTitle(item as INotifyPropertyChanged);
                UpdateTitle();
            }, Dispatcher, ReportError));
        Bindings.Own(new PropertyBinding<WinoAppShellViewModel, IShellMenuProvider?>(ViewModel, nameof(ViewModel.CurrentProvider),
            vm => vm.CurrentProvider, ObserveProvider, Dispatcher, ReportError));
        Bindings.Own(new PropertyBinding<WinoAppShellViewModel, WinoApplicationMode>(ViewModel, nameof(ViewModel.CurrentMode),
            vm => vm.CurrentMode, mode => { ShowMode(mode); UpdateTitle(); }, Dispatcher, ReportError));
        _state.PropertyChanged += StateChanged;
        Bindings.Own(new ActionDisposable(() =>
        {
            _state.PropertyChanged -= StateChanged;
            _window.SizeClassChanged -= SizeClassChanged;
            ObserveProvider(null);
            ObserveTitle(null);
            _sidebar.Dispose();
        }));
        RegisterDebugCommands();
        return _split;
    }

    protected override async Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        ViewModel.OnNavigatedTo(mode, parameter!);
        AttachShortcuts();
        var start = ShellModeActivator.StartMode(_state, _navigation.IsModeAvailable, ViewModel.PreferencesService.HiddenApplicationModes);
        await ActivateModeAsync(start, new ShellModeActivationContext { IsInitialActivation = true });
    }

    protected override async Task DeactivateAsync()
    {
        DetachShortcuts();
        var child = _child;
        _child = null;
        if (child is not null) await child.ReleaseAsync();
        ViewModel.ShutdownProviders();
        await base.DeactivateAsync();
    }

    /// <summary>Switches the shell to <paramref name="mode"/> (the Windows WinoAppShell.ActivateMode, through the shared activator).</summary>
    public async Task ActivateModeAsync(WinoApplicationMode mode, ShellModeActivationContext? context)
    {
        if (await _modes.ActivateAsync(mode, context, ClearContentAsync, Dispatcher, () => _child is not null))
            await Dispatcher.ExecuteOnUIThread(UpdateTitle);
    }

    /// <summary>Shows <paramref name="page"/> in the content area, releasing the previous one, then activates it.</summary>
    public async Task SetContentAsync(ILinuxPage page, object? parameter)
    {
        var previous = _child;
        _child = page;
        await Dispatcher.ExecuteOnUIThread(() =>
        {
            if (_contentHost.GetFirstChild() is { } old) _contentHost.Remove(old);
            var widget = page.Widget;
            widget.Hexpand = widget.Vexpand = true;
            _contentHost.Append(widget);
        });
        if (previous is not null) await previous.ReleaseAsync();
        await page.ActivateAsync(NavigationMode.New, parameter);
    }

    public async Task ClearContentAsync()
    {
        var previous = _child;
        _child = null;
        await Dispatcher.ExecuteOnUIThread(() => { if (_contentHost.GetFirstChild() is { } old) _contentHost.Remove(old); });
        if (previous is not null) await previous.ReleaseAsync();
    }

    private void SizeClassChanged(object? sender, WinoSizeClass sizeClass) => ApplySizeClass(sizeClass);

    private void ApplySizeClass(WinoSizeClass sizeClass)
    {
        var collapse = sizeClass != WinoSizeClass.Large;
        if (_split.Collapsed == collapse) return;
        _split.Collapsed = collapse;
        _split.ShowSidebar = !collapse && ViewModel.PreferencesService.IsNavigationPaneOpened;
    }

    #region Pane header

    private Gtk.MenuButton MainMenu()
    {
        var menu = Gio.Menu.New();
        menu.Append(string.Format(Translator.MacOSMenu_Quit, "WinoLux Mail"), "app.quit");
        var button = Gtk.MenuButton.New();
        button.IconName = "open-menu-symbolic";
        button.MenuModel = menu;
        button.Primary = true;
        button.TooltipText = Translator.MenuSettings;
        return button;
    }

    private async void Synchronize()
    {
        try { if (_provider is { CanSynchronize: true } provider) await provider.SynchronizeAsync(); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void ObserveProvider(IShellMenuProvider? provider)
    {
        if (_provider is not null) _provider.PropertyChanged -= ProviderChanged;
        _provider = provider;
        if (provider is not null) provider.PropertyChanged += ProviderChanged;
        ApplySynchronization();
    }

    private async void ProviderChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or nameof(IShellMenuProvider.IsSynchronizationSupported) or nameof(IShellMenuProvider.CanSynchronize)
            or nameof(IShellMenuProvider.SynchronizationState) or nameof(IShellMenuProvider.SynchronizationDescription) or nameof(IShellMenuProvider.SynchronizationToolTip))) return;
        try { await Dispatcher.ExecuteOnUIThread(ApplySynchronization); }
        catch (Exception exception) { ReportError(exception); }
    }

    private void ApplySynchronization()
    {
        if (_sync is null || _syncSpinner is null) return;
        var provider = _provider;
        var state = provider?.SynchronizationState;
        var syncing = state?.IsSynchronizing == true;
        _sync.Visible = provider?.IsSynchronizationSupported == true;
        _sync.Sensitive = provider is not null && (provider.CanSynchronize || syncing);
        _syncSpinner.Visible = syncing;
        if (_sync.Child is Gtk.Box box && box.GetFirstChild() is { } glyph) glyph.Visible = !syncing;
        var tip = syncing && !string.IsNullOrWhiteSpace(provider?.SynchronizationDescription)
            ? provider.SynchronizationDescription
            : provider?.SynchronizationToolTip;
        _sync.TooltipText = string.IsNullOrWhiteSpace(tip) ? Translator.Buttons_Sync : tip;
    }

    private void ObserveTitle(INotifyPropertyChanged? source)
    {
        if (_titleSource is not null) _titleSource.PropertyChanged -= TitleSourceChanged;
        _titleSource = source;
        if (source is not null) source.PropertyChanged += TitleSourceChanged;
    }

    private async void TitleSourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or nameof(IBaseFolderMenuItem.UnreadItemCount) or nameof(IBaseFolderMenuItem.FolderName))) return;
        try { await Dispatcher.ExecuteOnUIThread(UpdateTitle); }
        catch (Exception exception) { ReportError(exception); }
    }

    private async void StateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or nameof(IStatePersistanceService.CoreWindowTitle) or nameof(IStatePersistanceService.AppModeTitle))) return;
        try { await Dispatcher.ExecuteOnUIThread(UpdateTitle); }
        catch (Exception exception) { ReportError(exception); }
    }

    /// <summary>The pane title: in Mail the selected folder, then its account and unread count; otherwise the mode title.</summary>
    private void UpdateTitle()
    {
        string title;
        string subtitle = string.Empty;
        if (_modes.ActiveMode is WinoApplicationMode.Mail or null && ViewModel.SelectedMenuItem is IBaseFolderMenuItem folder)
        {
            title = folder.FolderName ?? string.Empty;
            var owner = folder is IFolderMenuItem { ParentAccount: { } account }
                ? (string.IsNullOrWhiteSpace(account.Address) ? account.Name : account.Address)
                : folder.AssignedAccountName;
            var unread = folder.ShowUnreadCount && folder.UnreadItemCount > 0
                ? $"{folder.UnreadItemCount} {Translator.Accessibility_MailItemReadState_Unread.ToLower(System.Globalization.CultureInfo.CurrentCulture)}"
                : null;
            subtitle = string.Join(" · ", new[] { owner, unread }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        else
        {
            title = !string.IsNullOrWhiteSpace(_state.AppModeTitle) ? _state.AppModeTitle : ShellModeActivator.ModeTitle(ViewModel.CurrentMode);
            subtitle = _state.CoreWindowTitle ?? string.Empty;
        }
        _title.Title = title;
        _title.Subtitle = subtitle;
        _window.Title = title;
    }

    #endregion

    #region Menu and modes

    private async void Select(IMenuItem item)
    {
        try
        {
            if (!ShellMenuItemRules.IsEnabled(item)) return;
            // The overlay pane closes after a pick, including the folder already shown.
            if (_split.Collapsed && item is IBaseFolderMenuItem) _split.ShowSidebar = false;
            // Folders select themselves when their navigation starts (MailAppShellViewModel.NavigateFolderAsync), as on the Mac.
            if (ViewModel.HandlesSelection && ShellMenuItemRules.SelectsOnInvoked(item) && item is not IBaseFolderMenuItem)
                ViewModel.SelectedMenuItem = item;
            await ViewModel.InvokeMenuItemAsync(item);
        }
        catch (Exception exception) { ReportError(exception); }
    }

    /// <summary>The Windows mode switcher at the pane's foot: Mail, Calendar, Contacts, To Do and Settings.</summary>
    private Gtk.Widget BuildModeSwitcher()
    {
        _modeSwitcher.Homogeneous = true;
        _modeSwitcher.AddCssClass("flat");
        _modeSwitcher.AddCssClass("wino-mode-switcher");
        foreach (var segment in WinoModeSegments.All)
        {
            var toggle = Adw.Toggle.New();
            toggle.Name = segment.Mode.ToString();
            toggle.Child = WinoIcons.Label(segment.Regular, 16);
            toggle.Tooltip = segment.Title;
            _modeSwitcher.Add(toggle);
        }
        _modeSwitcher.ActiveName = WinoApplicationMode.Mail.ToString();
        _modeSwitcher.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active-name" || _applyingMode) return;
            if (Enum.TryParse<WinoApplicationMode>(_modeSwitcher.ActiveName, out var mode)) SwitchMode(mode);
        };
        return _modeSwitcher;
    }

    private void SwitchMode(WinoApplicationMode mode)
    {
        if (mode != ViewModel.CurrentMode && !_navigation.IsModeAvailable(mode))
        {
            // Calendar, Contacts, To Do and Settings arrive in later milestones.
            _window.ShowToast(Adw.Toast.New($"{WinoModeSegments.All.First(segment => segment.Mode == mode).Title}: {Translator.ComingSoon}"));
            ShowMode(ViewModel.CurrentMode);
            return;
        }
        _navigation.ChangeApplicationMode(mode);
    }

    private void ShowMode(WinoApplicationMode mode)
    {
        _applyingMode = true;
        try { _modeSwitcher.ActiveName = mode.ToString(); }
        finally { _applyingMode = false; }
    }

    #endregion

    private void RegisterDebugCommands()
    {
#if DEBUG
        LinuxDebugBridge.Register("shell-state", _ => Task.FromResult(
            $"mode={ViewModel.CurrentMode} selected={(ViewModel.SelectedMenuItem as IBaseFolderMenuItem)?.FolderName ?? ViewModel.SelectedMenuItem?.GetType().Name ?? "none"} " +
            $"title='{_title.Title}' subtitle='{_title.Subtitle}' collapsed={_split.Collapsed} sidebar={_split.ShowSidebar} size={_window.SizeClass} " +
            $"content={_child?.GetType().Name ?? "none"} sync={_sync?.Visible}/{_sync?.Sensitive}\nrows: {_sidebar?.Describe()}"));
        // "folder NAME" invokes the first shown folder with that name, as a click would.
        LinuxDebugBridge.Register("folder", args =>
        {
            var name = string.Join(' ', args);
            var match = _sidebar?.Items.OfType<IBaseFolderMenuItem>().FirstOrDefault(folder => string.Equals(folder.FolderName, name, StringComparison.OrdinalIgnoreCase));
            if (match is null) return Task.FromResult("not found");
            Select(match);
            return Task.FromResult("ok");
        });
        LinuxDebugBridge.Register("mode", args =>
        {
            if (args.Length == 0 || !Enum.TryParse<WinoApplicationMode>(args[0], true, out var mode)) return Task.FromResult("usage: mode Mail|Calendar|Contacts|Tasks|Settings");
            _modeSwitcher.ActiveName = mode.ToString();
            return Task.FromResult($"active={_modeSwitcher.ActiveName}");
        });
        LinuxDebugBridge.Register("sidebar-toggle", _ =>
        {
            ToggleSidebar();
            return Task.FromResult($"sidebar={_split.ShowSidebar}");
        });
        // "window-size W H": GTK 4 resizes a shown window through its default size.
        LinuxDebugBridge.Register("window-size", args =>
        {
            if (args.Length < 2 || !int.TryParse(args[0], out var width) || !int.TryParse(args[1], out var height)) return Task.FromResult("usage: window-size WIDTH HEIGHT");
            _window.SetSize(width, height);
            return Task.FromResult("ok");
        });
        LinuxDebugBridge.Register("sync", _ =>
        {
            Synchronize();
            return Task.FromResult("ok");
        });
#endif
    }
}
