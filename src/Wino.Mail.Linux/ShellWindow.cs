using Wino.Core.Domain;

namespace Wino.Mail.Linux;

// Milestone 1 toolchain window: proves GTK 4, libadwaita, and the shared domain load together. The real shell
// (sidebar, list, reader) replaces it in milestone 2.
internal static class ShellWindow
{
    private const string ApplicationDisplayName = "WinoLux Mail";

    public static void Present(Adw.Application application)
    {
        var statusPage = Adw.StatusPage.New();
        statusPage.IconName = "mail-unread-symbolic";
        statusPage.Title = ApplicationDisplayName;
        statusPage.Description = Translator.NoMailSelected;

        var toolbarView = Adw.ToolbarView.New();
        toolbarView.AddTopBar(Adw.HeaderBar.New());
        toolbarView.Content = statusPage;

        var window = Adw.ApplicationWindow.New(application);
        window.Title = ApplicationDisplayName;
        window.SetDefaultSize(1100, 720);
        window.Content = toolbarView;
        window.Present();
    }
}
