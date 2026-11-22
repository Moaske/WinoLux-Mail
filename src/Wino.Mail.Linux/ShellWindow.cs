namespace Wino.Mail.Linux;

/// <summary>Wino's size classes in app-window pixels (design decisions, Size classes).</summary>
public enum WinoSizeClass
{
    /// <summary>1008 px and wider: sidebar, list and reader side by side.</summary>
    Large,
    /// <summary>641 to 1007 px: the sidebar becomes an overlay.</summary>
    Medium,
    /// <summary>640 px and narrower: list and reader become a drill-in.</summary>
    Small
}

/// <summary>
/// The main window: one routed page at a time (onboarding pages, or the shell), with toasts on top. Onboarding pages
/// sit under the window's header bar; the shell brings a header bar per pane, as GNOME split layouts do. Before the
/// first page it shows a status page: starting, or why startup failed.
/// </summary>
public sealed class ShellWindow
{
    private const string ApplicationDisplayName = "WinoLux Mail";
    private readonly Adw.ApplicationWindow _window;
    private readonly Adw.StatusPage _status;
    private readonly Adw.ToastOverlay _toasts;
    private readonly Adw.ToolbarView _content;
    private readonly Adw.Breakpoint _medium;
    private readonly Adw.Breakpoint _small;

    public ShellWindow(Adw.Application application)
    {
        _status = Adw.StatusPage.New();
        _status.IconName = "mail-unread-symbolic";
        _status.Title = ApplicationDisplayName;

        _content = Adw.ToolbarView.New();
        _content.AddTopBar(Adw.HeaderBar.New());
        _content.Content = _status;

        _toasts = Adw.ToastOverlay.New();
        _toasts.Child = _content;

        _window = Adw.ApplicationWindow.New(application);
        _window.Title = ApplicationDisplayName;
        _window.SetDefaultSize(1100, 720);
        // Breakpoints need a minimum size; GNOME's smallest supported window is 360 by 294.
        _window.WidthRequest = 360;
        _window.HeightRequest = 294;
        _window.Content = _toasts;

        // When several breakpoints match, the last added one applies.
        _medium = Adw.Breakpoint.New(Adw.BreakpointCondition.Parse("max-width: 1007px"));
        _small = Adw.Breakpoint.New(Adw.BreakpointCondition.Parse("max-width: 640px"));
        _window.AddBreakpoint(_medium);
        _window.AddBreakpoint(_small);
        _window.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "current-breakpoint") return;
            var next = ReadSizeClass();
            if (next == SizeClass) return;
            SizeClass = next;
            SizeClassChanged?.Invoke(this, next);
        };
    }

    /// <summary>The window as the parent of dialogs.</summary>
    public Gtk.Widget Widget => _window;

    public WinoSizeClass SizeClass { get; private set; } = WinoSizeClass.Large;

    /// <summary>Raised on the UI thread when the window crosses a size class.</summary>
    public event EventHandler<WinoSizeClass>? SizeClassChanged;

    public string Title
    {
        get => _window.Title ?? ApplicationDisplayName;
        set => _window.Title = string.IsNullOrWhiteSpace(value) ? ApplicationDisplayName : $"{value} – {ApplicationDisplayName}";
    }

    public void Present() => _window.Present();

    /// <summary>Resizes the window (GTK 4 resizes a shown window through its default size).</summary>
    public void SetSize(int width, int height) => _window.SetDefaultSize(width, height);

    /// <summary>Shows a toast over the window's content. UI thread.</summary>
    public void ShowToast(Adw.Toast toast) => _toasts.AddToast(toast);

    /// <summary>
    /// Makes <paramref name="page"/> the window content. A page with its own header bars (the shell) hides the
    /// window's. UI thread.
    /// </summary>
    public void ShowPage(Gtk.Widget page, bool hasOwnHeaderBars = false)
    {
        _content.RevealTopBars = !hasOwnHeaderBars;
        _content.Content = page;
        if (!hasOwnHeaderBars) _window.Title = ApplicationDisplayName;
    }

    public void ShowStartupFailure(Exception error)
    {
        _status.IconName = "dialog-error-symbolic";
        _status.Title = ApplicationDisplayName;
        _status.Description = GLib.Functions.MarkupEscapeText(error.Message, -1);
        ShowPage(_status);
    }

    private WinoSizeClass ReadSizeClass()
    {
        var current = _window.CurrentBreakpoint;
        if (current is null) return WinoSizeClass.Large;
        return current.Handle.DangerousGetHandle() == _small.Handle.DangerousGetHandle() ? WinoSizeClass.Small : WinoSizeClass.Medium;
    }
}
