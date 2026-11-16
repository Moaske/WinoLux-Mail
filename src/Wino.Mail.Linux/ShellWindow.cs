namespace Wino.Mail.Linux;

/// <summary>
/// The main window: a header bar over one routed page at a time (onboarding pages, or the shell), with toasts on top.
/// Before the first page it shows a status page: starting, or why startup failed.
/// </summary>
public sealed class ShellWindow
{
    private const string ApplicationDisplayName = "WinoLux Mail";
    private readonly Adw.ApplicationWindow _window;
    private readonly Adw.StatusPage _status;
    private readonly Adw.ToastOverlay _toasts;
    private readonly Adw.ToolbarView _content;

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
        _window.Content = _toasts;
    }

    /// <summary>The window as the parent of dialogs.</summary>
    public Gtk.Widget Widget => _window;

    public void Present() => _window.Present();

    /// <summary>Shows a toast over the window's content. UI thread.</summary>
    public void ShowToast(Adw.Toast toast) => _toasts.AddToast(toast);

    /// <summary>Makes <paramref name="page"/> the window content. UI thread.</summary>
    public void ShowPage(Gtk.Widget page) => _content.Content = page;

    public void ShowStartupFailure(Exception error)
    {
        _status.IconName = "dialog-error-symbolic";
        _status.Title = ApplicationDisplayName;
        _status.Description = error.Message;
        _content.Content = _status;
    }
}
