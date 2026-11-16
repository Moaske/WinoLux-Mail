using System.Collections.Specialized;
using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Accounts;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>Account setup progress (Windows AccountSetupProgressPage): each step with its state, the result, Go back and Try again.</summary>
public sealed class AccountSetupProgressPage(AccountSetupProgressPageViewModel viewModel, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<AccountSetupProgressPageViewModel>(viewModel, dispatcher, logger)
{
    private readonly Gtk.ListBox _steps = Gtk.ListBox.New();

    public override bool HasPendingWork => base.HasPendingWork || ViewModel.TryAgainCommand.IsRunning;

    protected override Gtk.Widget BuildView()
    {
        _steps.AddCssClass("boxed-list");
        _steps.SelectionMode = Gtk.SelectionMode.None;

        var success = new InfoBar(InfoBarSeverity.Success, null, Translator.AccountSetup_SuccessMessage);
        Form.Visible(success.Widget, nameof(ViewModel.IsSetupComplete), vm => vm.IsSetupComplete);
        var failure = new InfoBar(InfoBarSeverity.Error);
        Form.Bind(nameof(ViewModel.FailureMessage), vm => vm.FailureMessage, text => failure.Message = text ?? string.Empty);
        Form.Visible(failure.Widget, nameof(ViewModel.IsSetupFailed), vm => vm.IsSetupFailed);

        var back = Form.Command(Translator.AccountSetup_GoBackButton, ViewModel.GoBackCommand);
        Form.Visible(back, nameof(ViewModel.IsSetupFailed), vm => vm.IsSetupFailed);
        var retry = Form.Command(Translator.AccountSetup_TryAgainButton, ViewModel.TryAgainCommand, cssClass: "suggested-action");
        Form.Visible(retry, nameof(ViewModel.IsSetupFailed), vm => vm.IsSetupFailed);

        void Changed(object? sender, NotifyCollectionChangedEventArgs args) => _ = Dispatcher.ExecuteOnUIThread(Rebuild);
        ViewModel.Steps.CollectionChanged += Changed;
        Bindings.Own(new ActionDisposable(() => ViewModel.Steps.CollectionChanged -= Changed));
        Rebuild();
#if DEBUG
        LinuxDebugBridge.Register("setup-state", async _ =>
        {
            var state = string.Empty;
            await Dispatcher.ExecuteOnUIThread(() => state = $"complete={ViewModel.IsSetupComplete} failed={ViewModel.IsSetupFailed} message={ViewModel.FailureMessage} steps=" +
                string.Join(", ", ViewModel.Steps.Select(step => $"{step.Title}:{step.Status}")));
            return state;
        });
#endif

        var content = GtkUi.Column(18, GtkUi.Heading(GtkUi.Label(Translator.AccountSetup_Title), null), _steps, success.Widget, failure.Widget);
        return GtkUi.Page(content, [back], [retry]);
    }

    private void Rebuild()
    {
        if (Bindings.IsDisposed) return;
        _steps.RemoveAll();
        foreach (var step in ViewModel.Steps.ToList()) _steps.Append(StepRow(step));
    }

    private Gtk.Widget StepRow(AccountSetupStepModel step)
    {
        var row = Adw.ActionRow.New();
        row.Title = step.Title ?? string.Empty;
        var stepForm = new GtkForm<AccountSetupStepModel>(step, Bindings, Dispatcher, ReportError);
        var spinner = Adw.Spinner.New();
        var icon = Gtk.Image.New();
        row.AddPrefix(spinner);
        row.AddPrefix(icon);
        stepForm.Bind(nameof(step.Status), s => s.Status, status =>
        {
            spinner.Visible = status == AccountSetupStepStatus.InProgress;
            icon.Visible = status != AccountSetupStepStatus.InProgress;
            icon.SetFromIconName(status switch
            {
                AccountSetupStepStatus.Succeeded => "emblem-ok-symbolic",
                AccountSetupStepStatus.Failed => "dialog-error-symbolic",
                _ => "content-loading-symbolic"
            });
            icon.Opacity = status == AccountSetupStepStatus.Pending ? 0.4 : 1;
        });
        stepForm.Bind(nameof(step.ErrorMessage), s => s.ErrorMessage, error => row.Subtitle = error ?? string.Empty);
        return row;
    }

    protected override Task InitializeAsync(NavigationMode mode, object? parameter) => ViewModel.InitializeAsync(mode, parameter!);
}
