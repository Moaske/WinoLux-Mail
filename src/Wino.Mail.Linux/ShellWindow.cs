using Wino.Core.Domain;
using Wino.Mail.ViewModels.Lifecycle;

namespace Wino.Mail.Linux;

/// <summary>
/// The main window. Milestone 2a shows the startup state in a status page: starting, the Welcome or shell target the
/// shared startup order chose, or why startup failed. The three-pane shell replaces the status page in milestone 2b.
/// </summary>
public sealed class ShellWindow
{
    private const string ApplicationDisplayName = "WinoLux Mail";
    private readonly Adw.ApplicationWindow _window;
    private readonly Adw.StatusPage _status;

    public ShellWindow(Adw.Application application)
    {
        _status = Adw.StatusPage.New();
        _status.IconName = "mail-unread-symbolic";
        _status.Title = ApplicationDisplayName;

        var toolbarView = Adw.ToolbarView.New();
        toolbarView.AddTopBar(Adw.HeaderBar.New());
        toolbarView.Content = _status;

        _window = Adw.ApplicationWindow.New(application);
        _window.Title = ApplicationDisplayName;
        _window.SetDefaultSize(1100, 720);
        _window.Content = toolbarView;
    }

    public void Present() => _window.Present();

    public void ShowStartup(ApplicationStartupTarget target)
    {
        if (target == ApplicationStartupTarget.Welcome)
        {
            _status.Title = Translator.WelcomeWindow_SetupTitle;
            _status.Description = Translator.WelcomeWindow_SetupSubtitle;
        }
        else
        {
            _status.Title = ApplicationDisplayName;
            _status.Description = Translator.NoMailSelected;
        }
    }

    public void ShowStartupFailure(Exception error)
    {
        _status.IconName = "dialog-error-symbolic";
        _status.Title = ApplicationDisplayName;
        _status.Description = error.Message;
    }
}
