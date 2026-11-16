using Microsoft.Extensions.DependencyInjection;
using Wino.Core.Domain;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;

namespace Wino.Mail.Linux.Views;

/// <summary>
/// Stands in for the three-pane shell until milestone 2c: the window shows that the accounts exist and lists them.
/// The coordinator treats it as the shell, so adding an account ends here like it ends in the shell on Windows.
/// </summary>
public sealed class ShellPlaceholderPage(IServiceProvider services) : ILinuxPage
{
    private readonly Adw.StatusPage _status = Adw.StatusPage.New();
    private Gtk.Widget? _widget;

    public Gtk.Widget Widget => _widget ??= Build();
    public bool HasPendingWork => false;
    public object AssociatedViewModel => this;

    private Gtk.Widget Build()
    {
        _status.IconName = "mail-unread-symbolic";
        _status.Title = "WinoLux Mail";
        _status.Description = Translator.NoMailSelected;
        _status.Vexpand = true;
        return _status;
    }

    public async Task ActivateAsync(NavigationMode mode, object? parameter)
    {
        var accounts = await services.GetRequiredService<IAccountService>().GetAccountsAsync();
        var lines = string.Join("\n", accounts.Select(account => string.IsNullOrWhiteSpace(account.Address) ? account.Name : $"{account.Name} · {account.Address}"));
        await services.GetRequiredService<IDispatcher>().ExecuteOnUIThread(() =>
            _status.Description = string.IsNullOrEmpty(lines) ? Translator.NoMailSelected : lines);
    }

    public Task ReleaseAsync() => Task.CompletedTask;
}
