using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.Linux.Presentation;
using Wino.Mail.ViewModels;
using Wino.Presentation.Bindings;

namespace Wino.Mail.Linux.Views.Onboarding;

/// <summary>
/// The provider wizard (Windows ProviderSelectionPage, Mac ProviderSelectionPageViewController): featured providers and
/// an "IMAP / SMTP" row that opens the searchable catalog, then the account name and colour, then the capabilities.
/// Continue follows the shared ViewModel. Outlook and Gmail sign in through OAuth registrations WinoLux does not have
/// yet, so they are not offered (L3 in the Linux design decisions).
/// </summary>
public sealed class ProviderSelectionPage(ProviderSelectionPageViewModel viewModel, IApplicationResourceResolver resources, IDispatcher dispatcher, IWinoLogger logger)
    : LinuxPage<ProviderSelectionPageViewModel>(viewModel, dispatcher, logger)
{
    private readonly Gtk.ListBox _featured = BoxedList();
    private readonly Gtk.ListBox _custom = BoxedList();
    private readonly Gtk.ListBox _catalog = BoxedList();
    private readonly Gtk.FlowBox _colors = Gtk.FlowBox.New();
    private readonly List<(IProviderDetail Provider, Gtk.Image Check)> _rows = [];
    private readonly List<Action> _refreshAfterNavigation = [];
    private readonly List<(string Hex, Gtk.ToggleButton Button)> _swatches = [];
    private BindingScope? _colorScope;

    protected override Gtk.Widget BuildView()
    {
        var vm = ViewModel;
        var step = Form.Label(nameof(vm.StepProgressText), s => s.StepProgressText, "dim-label", "caption");
        var title = Form.Label(nameof(vm.PageTitle), s => s.PageTitle);
        var subtitle = Form.Label(nameof(vm.PageSubtitle), s => s.PageSubtitle);

        var back = Form.Command(Translator.Buttons_Back, vm.GoBackCommand);
        Form.Visible(back, nameof(vm.CanGoBack), s => s.CanGoBack);
        var next = Form.Command(Translator.Buttons_Continue, vm.ContinueCommand, cssClass: "suggested-action");
        Form.Bind(nameof(vm.ContinueButtonText), s => s.ContinueButtonText, text => next.Label = text);

        var content = GtkUi.Column(24, GtkUi.Column(6, step, GtkUi.Heading(title, subtitle)),
            BuildProviderStep(), BuildSummary(), BuildIdentityStep(), BuildCapabilityStep());
        return GtkUi.Page(content, [back], [next]);
    }

    #region Provider step

    private Gtk.Widget BuildProviderStep()
    {
        var vm = ViewModel;
        var featured = Adw.PreferencesGroup.New();
        featured.Title = Translator.ProviderSelection_ProviderSectionTitle;
        featured.Description = Translator.ProviderSelection_ProviderSectionDescription;
        featured.Add(_featured);
        var more = Adw.ActionRow.New();
        more.Title = Translator.ProviderSelection_MoreProviders_Title;
        more.Subtitle = Translator.ProviderSelection_MoreProviders_Description;
        more.Activatable = true;
        more.AddSuffix(Gtk.Image.NewFromIconName("go-next-symbolic"));
        void ShowCatalog(Adw.ActionRow sender, EventArgs args) => vm.ShowCatalogCommand.Execute(null);
        more.OnActivated += ShowCatalog;
        Bindings.Own(new ActionDisposable(() => more.OnActivated -= ShowCatalog));
        var moreList = BoxedList();
        moreList.Append(more);
        moreList.MarginTop = 12;
        featured.Add(moreList);
        Form.Visible(featured, nameof(vm.IsFeaturedListVisible), s => s.IsFeaturedListVisible);

        var search = Gtk.SearchEntry.New();
        search.PlaceholderText = Translator.ProviderSelection_Catalog_SearchPlaceholder;
        search.Hexpand = true;
        var searchBinding = Form.Bind(nameof(vm.CatalogSearchText), s => s.CatalogSearchText ?? string.Empty,
            text => { if (search.GetText() != text) search.SetText(text); }, (s, v) => s.CatalogSearchText = v);
        void Searched(Gtk.SearchEntry sender, EventArgs args) => searchBinding.UpdateSource(search.GetText());
        search.OnSearchChanged += Searched;
        Bindings.Own(new ActionDisposable(() => search.OnSearchChanged -= Searched));
        var count = Form.Label(nameof(vm.CatalogResultCountText), s => s.CatalogResultCountText, "dim-label", "caption");
        _refreshAfterNavigation.Add(Form.Bind(nameof(vm.CatalogResultCountText), s => s.CatalogResultCountText, text => count.SetText(text ?? string.Empty)).Refresh);
        var empty = Form.Label(nameof(vm.CatalogNoResultsText), s => s.CatalogNoResultsText, "dim-label");
        Form.Visible(empty, nameof(vm.IsCatalogEmpty), s => s.IsCatalogEmpty);

        var catalog = Adw.PreferencesGroup.New();
        catalog.Title = Translator.ProviderSelection_Catalog_SectionTitle;
        catalog.Description = Translator.ProviderSelection_Catalog_SectionDescription;
        catalog.Add(GtkUi.Column(12, _custom, GtkUi.Label(Translator.ProviderSelection_Catalog_KnownProvidersHeader, "heading"),
            GtkUi.Row(12, search, count), _catalog, empty));
        Form.Visible(catalog, nameof(vm.IsCatalogVisible), s => s.IsCatalogVisible);

        Form.Bind(nameof(vm.FeaturedProviders), s => s.FeaturedProviders, providers => Fill(_featured, providers));
        Form.Bind(nameof(vm.CustomServerProviders), s => s.CustomServerProviders, providers => Fill(_custom, providers));
        _refreshAfterNavigation.Add(Form.Bind(nameof(vm.FilteredCatalogProviders), s => s.FilteredCatalogProviders, providers => Fill(_catalog, providers)).Refresh);
        Form.Bind(nameof(vm.SelectedProvider), s => s.SelectedProvider, UpdateSelection);

        var stepBox = GtkUi.Column(12, featured, catalog);
        Form.Visible(stepBox, nameof(vm.IsProviderStepVisible), s => s.IsProviderStepVisible);
        return stepBox;
    }

    /// <summary>Replaces the rows of one list; OAuth providers are left out until WinoLux has its own sign-in registrations.</summary>
    private void Fill(Gtk.ListBox list, IReadOnlyList<IProviderDetail>? providers)
    {
        _rows.RemoveAll(row => row.Check.GetParent() is null || IsInList(row.Check, list));
        list.RemoveAll();
        foreach (var provider in (providers ?? []).Where(static provider => provider.Type is not (MailProviderType.Outlook or MailProviderType.Gmail)))
        {
            var row = Adw.ActionRow.New();
            row.Title = provider.Name;
            if (!string.IsNullOrWhiteSpace(provider.Description)) row.Subtitle = provider.Description;
            row.Activatable = true;
            var logo = Gtk.Image.NewFromFile(resources.ResolvePackagedResource(provider.ProviderImage.TrimStart('/')).LocalPath);
            logo.PixelSize = 32;
            row.AddPrefix(logo);
            var check = Gtk.Image.NewFromIconName("object-select-symbolic");
            check.AddCssClass("accent");
            check.Visible = ReferenceEquals(provider, ViewModel.SelectedProvider);
            row.AddSuffix(check);
            void Activated(Adw.ActionRow sender, EventArgs args)
            {
                if (ViewModel.SelectProviderCommand.CanExecute(provider)) ViewModel.SelectProviderCommand.Execute(provider);
            }
            row.OnActivated += Activated;
            _rows.Add((provider, check));
            list.Append(row);
        }
    }

    private static bool IsInList(Gtk.Widget widget, Gtk.ListBox list)
    {
        for (var parent = widget.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent.Handle == list.Handle) return true;
        return false;
    }

    private void UpdateSelection(IProviderDetail? selected)
    {
        foreach (var (provider, check) in _rows) check.Visible = ReferenceEquals(provider, selected);
    }

    #endregion

    #region Summary, identity, capabilities

    /// <summary>The chosen provider and account name, on every step after the first.</summary>
    private Gtk.Widget BuildSummary()
    {
        var vm = ViewModel;
        var row = Adw.ActionRow.New();
        var logo = Gtk.Image.New();
        logo.PixelSize = 32;
        row.AddPrefix(logo);
        Form.Bind(nameof(vm.SelectedProvider), s => s.SelectedProvider, provider =>
        {
            row.Title = vm.SelectedProviderName;
            if (provider is not null) logo.SetFromFile(resources.ResolvePackagedResource(provider.ProviderImage.TrimStart('/')).LocalPath);
        });
        Form.Bind(nameof(vm.SelectedProviderSummaryDetail), s => s.SelectedProviderSummaryDetail, detail => row.Subtitle = detail ?? string.Empty);
        var list = BoxedList();
        list.Append(row);
        Form.Visible(list, nameof(vm.IsAccountSummaryVisible), s => s.IsAccountSummaryVisible);
        return list;
    }

    private Gtk.Widget BuildIdentityStep()
    {
        var vm = ViewModel;
        var name = Form.Text(nameof(vm.AccountName), s => s.AccountName, (s, v) => s.AccountName = v,
            Translator.ProviderSelection_AccountNamePlaceholder, Translator.ProviderSelection_AccountNameHeader);
        var nameGroup = Adw.PreferencesGroup.New();
        nameGroup.Title = Translator.ProviderSelection_AccountNameHeader;
        nameGroup.Description = Translator.ProviderSelection_AccountNameDescription;
        nameGroup.Add(name);

        _colors.SelectionMode = Gtk.SelectionMode.None;
        _colors.MaxChildrenPerLine = 15;
        _colors.ColumnSpacing = 6;
        _colors.RowSpacing = 6;
        var clear = Form.Command(Translator.ProviderSelection_ClearColor, vm.ClearColorCommand);
        clear.Halign = Gtk.Align.Start;
        var colorGroup = Adw.PreferencesGroup.New();
        colorGroup.Title = Translator.AccountDetailsPage_ColorPicker_Title;
        colorGroup.Description = Translator.AccountDetailsPage_ColorPicker_Description;
        colorGroup.Add(GtkUi.Column(12, _colors, clear));
        Form.Bind(nameof(vm.SelectedColor), s => s.SelectedColor?.Hex, UpdateSwatches);

        var stepBox = GtkUi.Column(24, nameGroup, colorGroup);
        Form.Visible(stepBox, nameof(vm.IsIdentityStepVisible), s => s.IsIdentityStepVisible);
        return stepBox;
    }

    /// <summary>One toggle per palette colour; the dot is coloured with Pango markup, so no style sheet is needed.</summary>
    private void BuildSwatches()
    {
        _colorScope?.Dispose();
        _colorScope = Bindings.Own(new BindingScope());
        _colors.RemoveAll();
        _swatches.Clear();
        foreach (var color in ViewModel.AvailableColors)
        {
            var dot = Gtk.Label.New(null);
            dot.SetMarkup($"<span foreground=\"{GLib.Functions.MarkupEscapeText(color.Hex, -1)}\" size=\"x-large\">●</span>");
            var button = Gtk.ToggleButton.New();
            button.Child = dot;
            button.AddCssClass("flat");
            button.AddCssClass("circular");
            button.TooltipText = color.Hex;
            var hex = color.Hex;
            void Toggled(Gtk.ToggleButton sender, EventArgs args)
            {
                if (!sender.Active || string.Equals(ViewModel.SelectedColor?.Hex, hex, StringComparison.OrdinalIgnoreCase)) return;
                var picked = ViewModel.AvailableColors.FirstOrDefault(item => string.Equals(item.Hex, hex, StringComparison.OrdinalIgnoreCase));
                if (picked is not null) ViewModel.SelectedColor = picked;
            }
            button.OnToggled += Toggled;
            _colorScope.Own(new ActionDisposable(() => button.OnToggled -= Toggled));
            _swatches.Add((hex, button));
            _colors.Append(button);
        }
        UpdateSwatches(ViewModel.SelectedColor?.Hex);
    }

    private void UpdateSwatches(string? selected)
    {
        foreach (var (hex, button) in _swatches)
        {
            var active = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase);
            if (button.Active != active) button.Active = active;
        }
    }

    private Gtk.Widget BuildCapabilityStep()
    {
        var vm = ViewModel;
        var range = Form.DropDown(vm.InitialSynchronizationRanges.Select(option => option.DisplayText), nameof(vm.SelectedInitialSynchronizationRange),
            s => s.InitialSynchronizationRanges.IndexOf(s.SelectedInitialSynchronizationRange),
            (s, index) => s.SelectedInitialSynchronizationRange = s.InitialSynchronizationRanges[index]);
        Form.Sensitive(range, nameof(vm.IsMailSynchronizationRangeVisible), s => s.IsMailSynchronizationRangeVisible);
        var rangeRow = GtkUi.FieldRow(Translator.ProviderSelection_MailRangeHeader, range, Translator.ProviderSelection_MailRangeDescription);
        var everything = new InfoBar(InfoBarSeverity.Warning, Translator.GeneralTitle_Warning, Translator.AccountCreation_InitialSynchronization_EverythingWarning);
        Form.Visible(everything.Widget, nameof(vm.IsInitialSynchronizationWarningVisible), s => s.IsInitialSynchronizationWarningVisible);

        var picker = CapabilityPicker.Create(vm.Capabilities, Bindings, Dispatcher, ReportError, rangeRow);
        picker.Title = Translator.ProviderSelection_CapabilitySectionTitle;
        picker.Description = Translator.ProviderSelection_CapabilityIntroDescription;

        var calendarOnly = new InfoBar(InfoBarSeverity.Informational, null, Translator.ProviderSelection_CalendarOnlyServerHint);
        Form.Visible(calendarOnly.Widget, nameof(vm.IsCalendarOnlyServerHintVisible), s => s.IsCalendarOnlyServerHintVisible);
        var localOnly = new InfoBar(InfoBarSeverity.Informational, null, Translator.ProviderSelection_LocalOnlyHint_Device);
        Form.Visible(localOnly.Widget, nameof(vm.IsLocalOnlyHintVisible), s => s.IsLocalOnlyHintVisible);
        var cardDav = new InfoBar(InfoBarSeverity.Informational, null, vm.DavContactAvailabilityMessage);
        Form.Visible(cardDav.Widget, nameof(vm.IsCardDavDiscoveryHintVisible), s => s.IsCardDavDiscoveryHintVisible);
        var missing = GtkUi.Label(Translator.ProviderSelection_CapabilityValidationMessage, "error");
        Form.Visible(missing, nameof(vm.IsCapabilitySelectionMissing), s => s.IsCapabilitySelectionMissing);

        var stepBox = GtkUi.Column(12, picker, everything.Widget, calendarOnly.Widget, localOnly.Widget, cardDav.Widget, missing);
        Form.Visible(stepBox, nameof(vm.IsCapabilityStepVisible), s => s.IsCapabilityStepVisible);
        return stepBox;
    }

    #endregion

    private static Gtk.ListBox BoxedList()
    {
        var list = Gtk.ListBox.New();
        list.AddCssClass("boxed-list");
        list.SelectionMode = Gtk.SelectionMode.None;
        return list;
    }

    protected override Task InitializeAsync(NavigationMode mode, object? parameter)
    {
        ViewModel.OnNavigatedTo(mode, parameter!);
        // The lists derive from the search text, which OnNavigatedTo may leave unchanged; the palette never raises a change.
        foreach (var refresh in _refreshAfterNavigation) refresh();
        BuildSwatches();
#if DEBUG
        RegisterDebugCommands();
#endif
        return Task.CompletedTask;
    }

#if DEBUG
    /// <summary>
    /// Debug bridge (the Mac provider commands): <c>provider NAME|TYPE</c>, <c>provider-catalog</c>, <c>provider-name NAME</c>
    /// (the account's display name), <c>provider-capability mail|calendar|contacts|tasks provider|local|off</c>,
    /// <c>provider-continue</c>, <c>provider-state</c>. Only selection and navigation; no credential is ever filled.
    /// </summary>
    private void RegisterDebugCommands()
    {
        LinuxDebugBridge.Register("provider", args => OnUi(() =>
        {
            var query = string.Join(' ', args);
            var provider = ViewModel.Providers.FirstOrDefault(item => string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase))
                ?? ViewModel.Providers.FirstOrDefault(item => item.SpecialImapProvider == SpecialImapProvider.None
                    && string.Equals(item.Type.ToString(), query, StringComparison.OrdinalIgnoreCase));
            if (provider is null) return "no provider " + query + "; known: " + string.Join(", ", ViewModel.Providers.Select(item => item.Name));
            if (!provider.IsFeatured && !ViewModel.IsCatalogVisible) ViewModel.ShowCatalogCommand.Execute(null);
            ViewModel.SelectProviderCommand.Execute(provider);
            return "selected " + provider.Name;
        }));
        LinuxDebugBridge.Register("provider-catalog", _ => OnUi(() => { ViewModel.ShowCatalogCommand.Execute(null); return "ok"; }));
        LinuxDebugBridge.Register("provider-name", args => OnUi(() => { ViewModel.AccountName = string.Join(' ', args); return "ok"; }));
        LinuxDebugBridge.Register("provider-capability", args => OnUi(() =>
        {
            if (args.Length < 2) return "usage: provider-capability mail|calendar|contacts|tasks provider|local|off";
            ViewModel.ChooseCapabilityCommand.Execute(args[0] + ":" + args[1]);
            return $"mail={ViewModel.MailMode} calendar={ViewModel.CalendarMode} contacts={ViewModel.ContactMode} tasks={ViewModel.TaskMode}";
        }));
        LinuxDebugBridge.Register("provider-continue", async _ =>
        {
            var canContinue = false;
            await Dispatcher.ExecuteOnUIThread(() => canContinue = ViewModel.ContinueCommand.CanExecute(null));
            if (!canContinue) return "cannot continue at " + ViewModel.CurrentStep;
            await ViewModel.ContinueCommand.ExecuteAsync(null);
            return "ok " + ViewModel.CurrentStep;
        });
        LinuxDebugBridge.Register("provider-state", _ => OnUi(() =>
            $"step={ViewModel.CurrentStep} catalog={ViewModel.IsCatalogVisible} provider={ViewModel.SelectedProvider?.Name} name={ViewModel.AccountName}"));
    }

    private async Task<string> OnUi(Func<string> action)
    {
        var result = string.Empty;
        await Dispatcher.ExecuteOnUIThread(() => result = action());
        return result;
    }
#endif
}
