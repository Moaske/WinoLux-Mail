namespace Wino.Mail.Linux;

public static class Program
{
    public const string ApplicationId = "io.github.moaske.WinoLux";

    public static int Main()
    {
        Adw.Module.Initialize();

        var application = Adw.Application.New(ApplicationId, Gio.ApplicationFlags.DefaultFlags);
        application.OnActivate += (_, _) => ShellWindow.Present(application);

        return application.RunWithSynchronizationContext(null);
    }
}
