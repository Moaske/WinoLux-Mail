using CommunityToolkit.Mvvm.Messaging;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Models.Navigation;
using Wino.Mail.Linux.Infrastructure;
using Wino.Mail.ViewModels.Data;
using Wino.Mail.ViewModels.Messages;
using Wino.Messaging.Client.Mails;

namespace Wino.Mail.Linux.Views.Mail;

/// <summary>
/// The reading pane as the rendering frame (the Mac MailListPageViewController.ReadingPane): one page at a time, the
/// reader or the idle page. The reader is reused across selections, like the Windows rendering frame: the next message
/// loads into the same web view. The selected message reaches the shared ViewModel through
/// <see cref="ActiveMailItemChangedEvent"/>, which opens it in this frame.
/// </summary>
public sealed partial class MailListPage : ILinuxRenderingFrameHost
{
    private readonly SemaphoreSlim _paneGate = new(1, 1);
    private readonly Adw.Bin _pane = Adw.Bin.New();
    private ILinuxPage? _paneChild;
    private int _paneVersion;

    private void AttachReadingPane()
    {
        _navigation.AttachRenderingHost(this);
        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<MailListPage, ActiveMailItemChangedEvent>(this, static (page, message) =>
        {
            var item = message.SelectedMailItemViewModel;
            page.OnUI(() => page.ApplyActiveMailItemChange(item));
        });
        messenger.Register<MailListPage, ClearMailSelectionsRequested>(this, static (page, _) => page.OnUI(page.ClearSelection));
        messenger.Register<MailListPage, DisposeRenderingFrameRequested>(this, static (page, _) => page.OnUI(page.ShowIdleContent));
        messenger.Register<MailListPage, SelectMailItemContainerEvent>(this, static (page, message) => page.Observe(page.SelectItemWhenReadyAsync(message)));
        ShowIdleContent();
    }

    private async Task DetachReadingPaneAsync()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _navigation.DetachRenderingHost(this);
        await ClearAsync();
    }

    public async Task ShowAsync(ILinuxPage page, object? parameter)
    {
        int version = Interlocked.Increment(ref _paneVersion);
        await _paneGate.WaitAsync();
        try
        {
            if (_released || version != Volatile.Read(ref _paneVersion))
            {
                await page.ReleaseAsync();
                return;
            }
            if (_paneChild is MailRenderingPage reader && page is MailRenderingPage)
            {
                await page.ReleaseAsync();
                await reader.RenavigateAsync(parameter);
                return;
            }
            if (_paneChild is IdlePage && page is IdlePage && parameter is null)
            {
                await page.ReleaseAsync();
                return;
            }
            await ReplacePaneChildAsync(page);
            await page.ActivateAsync(NavigationMode.New, parameter);
        }
        finally { _paneGate.Release(); }
    }

    public async Task ClearAsync()
    {
        Interlocked.Increment(ref _paneVersion);
        await _paneGate.WaitAsync();
        try { await ReplacePaneChildAsync(null); }
        finally { _paneGate.Release(); }
    }

    /// <summary>Releases the current page (asynchronously) and shows the next one. The caller holds the gate.</summary>
    private async Task ReplacePaneChildAsync(ILinuxPage? next)
    {
        var previous = _paneChild;
        _paneChild = next;
        await Dispatcher.ExecuteOnUIThread(() => _pane.Child = next?.Widget);
        if (previous is not null)
        {
            try { await previous.ReleaseAsync(); }
            catch (Exception exception) { ReportError(exception); }
        }
    }

    private void ShowIdleContent()
    {
        if (_released) return;
        ViewModel.NavigationService.Navigate(WinoPage.IdlePage, null, NavigationReferenceFrame.RenderingFrame);
    }

    private void ApplyActiveMailItemChange(MailItemViewModel? item)
    {
        if (item is null)
        {
            if (_paneChild is not IdlePage) ShowIdleContent();
            return;
        }
        ViewModel.OpenItem(item);
        // The narrow layout shows one pane: opening a message moves to the reader.
        _split.ShowContent = true;
    }

    private void ClearSelection()
    {
        if (!_listBound) return;
        _selection.UnselectAll();
    }

    /// <summary>Selects a message once it is listed (a notification or a search result can arrive before the list).</summary>
    private async Task SelectItemWhenReadyAsync(SelectMailItemContainerEvent message)
    {
        for (int attempt = 0; attempt < 15 && !_released; attempt++)
        {
            bool selected = false;
            await Dispatcher.ExecuteOnUIThread(() =>
            {
                if (_released || !_listBound) return;
                int index = _entries.FindIndex(entry => entry.Row is { } row &&
                    (row.SourceItem.StableId == message.MailUniqueId || (row.IsThreadHead && row.LeafItems.Any(leaf => leaf.StableId == message.MailUniqueId))));
                if (index < 0) return;
                _selection.SelectItem((uint)index, true);
                if (message.ScrollToItem) _listView.ScrollTo((uint)index, Gtk.ListScrollFlags.None, null);
                selected = true;
            });
            if (selected) return;
            await Task.Delay(200);
        }
    }
}
