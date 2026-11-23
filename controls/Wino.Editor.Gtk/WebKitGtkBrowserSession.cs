using System.Text.Json;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Platform;

namespace Wino.Editor.Gtk;

/// <summary>
/// The WebKitGTK twin of the Apple WebKitBrowserSession: owns the script message handler, the navigation guard and one
/// current document generation. Each load is a fresh shell from <see cref="AppleMailDocumentBuilder"/> (scripts and
/// styles inline, a per-document nonce and the remote-content CSP) at <c>about:blank</c>; the shell's scripts post to
/// <c>window.webkit.messageHandlers.winoMail</c>, which WebKitGTK provides like WKWebView. Every navigation after the
/// shell load is cancelled; links leave through <see cref="LinkOpener"/> or the launcher. Native state is touched on the
/// UI thread only (through <see cref="IDispatcher"/>).
/// </summary>
internal sealed class WebKitGtkBrowserSession
{
    private const string HandlerName = "winoMail";
    private readonly IDispatcher _dispatcher;
    private readonly WebKit.UserContentManager _contents;
    private readonly CancellationTokenSource _linkLifetime = new();
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _sessionId;
    private bool _allowShell;
    private bool _disposed;
    private IExternalLauncher? _launcher;

    public WebKitGtkBrowserSession(WebKit.WebView browser, IDispatcher dispatcher)
    {
        Browser = browser;
        _dispatcher = dispatcher;
        var settings = browser.GetSettings();
        settings.EnableJavascript = true;
        settings.JavascriptCanOpenWindowsAutomatically = false;
        settings.EnableDeveloperExtras = false;
        settings.EnableWebgl = false;
        settings.EnableWebaudio = false;
        settings.EnableMediaStream = false;
        settings.EnableBackForwardNavigationGestures = false;
        settings.AllowFileAccessFromFileUrls = false;
        settings.AllowUniversalAccessFromFileUrls = false;

        _contents = browser.GetUserContentManager();
        WebKit.UserContentManager.ScriptMessageReceivedSignal.Connect(_contents, ScriptMessageReceived, true, HandlerName);
        if (!_contents.RegisterScriptMessageHandler(HandlerName, null))
            throw new InvalidOperationException("The mail document bridge could not be registered.");
        WebKit.WebView.DecidePolicySignal.Connect(browser, DecidePolicy);
        WebKit.WebView.WebProcessTerminatedSignal.Connect(browser, WebProcessTerminated);
    }

    public WebKit.WebView Browser { get; }
    public SessionOperationQueue Queue { get; } = new();
    public bool IsReady => !_disposed && _sessionId is not null && _ready.Task.IsCompletedSuccessfully;
    public event EventHandler<EditorMessage>? Message;
    public event EventHandler<RendererNavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler<Exception>? OperationFailed;

    /// <summary>The reader's hovered or focused link changed (the host shows its address).</summary>
    public event EventHandler<RendererLinkHoverEventArgs>? LinkHovered;

    /// <summary>Opens clicked links in place of the launcher (the reader's link cleaning and confirmation).</summary>
    public Func<Uri, string?, Task<PlatformOperationResult>>? LinkOpener { get; set; }

    public void Configure(IExternalLauncher launcher)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
    }

    public async Task LoadAsync(bool editor, RemoteContentPolicy policy, bool dark, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        // Ctrl-based shortcuts, as on Android: Command does not exist here.
        string document = editor ? await AppleMailDocumentBuilder.BuildEditorAsync(id, dark, token, useCommandKey: false)
            : await AppleMailDocumentBuilder.BuildReaderAsync(id, policy, dark, token, useCommandKey: false);
        await OnUIAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sessionId = id;
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _allowShell = true;
            Browser.StopLoading();
            Browser.LoadHtml(document, null);
            return Task.CompletedTask;
        });
        try { await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
        catch
        {
            await OnUIAsync(() => { _sessionId = null; Browser.StopLoading(); return Task.CompletedTask; });
            throw;
        }
    }

    public async Task<string?> EvaluateAsync(string expression)
    {
        if (!IsReady) throw new InvalidOperationException("The mail document is not ready.");
        string? generation = _sessionId;
        return await OnUIAsync(async () =>
        {
            // JavaScriptCore values are GObjects: release each one at once, or the garbage collector may drop the
            // wrapper while WebKit still references the value (Gir.Core then warns about a toggle reference).
            using var result = await Browser.EvaluateJavascriptAsync("String((" + expression + ") ?? '')");
            if (_disposed || generation != _sessionId) throw new ObjectDisposedException(nameof(WebKitGtkBrowserSession));
            return result.ToString();
        });
    }

    public Task OnUIAsync(Func<Task> operation) => OnUIAsync(async () => { await operation(); return true; });

    /// <summary>Runs <paramref name="operation"/> on the UI thread; its awaits resume there (the GLib synchronization context).</summary>
    public async Task<T> OnUIAsync<T>(Func<Task<T>> operation)
    {
        Task<T>? running = null;
        await _dispatcher.ExecuteOnUIThread(() => running = operation());
        return await (running ?? throw new InvalidOperationException("The UI thread did not run the operation."));
    }

    private void ScriptMessageReceived(WebKit.UserContentManager manager, WebKit.UserContentManager.ScriptMessageReceivedSignalArgs args)
    {
        if (_disposed) return;
        // Only the shell document at about:blank talks to the app; WebKitGTK does not expose the sending frame, and
        // the shell allows no frames (CSP frame-src 'none').
        if (Browser.GetUri() is not (null or "about:blank")) return;
        try
        {
            string json;
            using (var value = args.Value) json = value.ToString();
            var message = JsonSerializer.Deserialize(json, EditorJsonContext.Default.EditorMessage);
            if (_sessionId is null || message?.SessionId != _sessionId) return;
            if (message.Type == "ready") _ready.TrySetResult();
            else if (message.Type is "bootstrapError" or "initializationError")
                _ready.TrySetException(new InvalidOperationException("The mail document failed to initialize."));
            else if (message.Type is "openLink" or "navigation")
            {
                var reader = JsonSerializer.Deserialize(json, EditorJsonContext.Default.RendererMessage);
                if (Uri.TryCreate(message.Url ?? reader?.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
                    OpenLink(uri, NullIfBlank(reader?.Text));
            }
            else if (message.Type == "linkHover")
            {
                var hover = JsonSerializer.Deserialize(json, EditorJsonContext.Default.RendererMessage);
                LinkHovered?.Invoke(this, new(NullIfBlank(hover?.Uri), NullIfBlank(hover?.Target)));
            }
            else Message?.Invoke(this, message);
        }
        catch (JsonException) { }
    }

    /// <summary>
    /// Only the shell load itself may navigate (once, to about:blank). Link clicks inside the mail are cancelled here;
    /// the shell's script reports them over the bridge, and new-window requests are refused the same way.
    /// </summary>
    private bool DecidePolicy(WebKit.WebView view, WebKit.WebView.DecidePolicySignalArgs args)
    {
        var decision = args.Decision;
        if (args.DecisionType == WebKit.PolicyDecisionType.Response)
        {
            decision.Use();
            return true;
        }
        string? uri = (decision as WebKit.NavigationPolicyDecision)?.GetNavigationAction()?.GetRequest()?.GetUri();
        bool allow = _allowShell && args.DecisionType == WebKit.PolicyDecisionType.NavigationAction && uri is null or "about:blank";
        _allowShell = false;
        if (allow) decision.Use();
        else decision.Ignore();
        return true;
    }

    /// <summary>A crashed web process leaves no document: the next operation loads a fresh shell.</summary>
    private void WebProcessTerminated(WebKit.WebView view, WebKit.WebView.WebProcessTerminatedSignalArgs args)
    {
        _sessionId = null;
        _ready.TrySetException(new InvalidOperationException($"The mail view stopped ({args.Reason})."));
        OperationFailed?.Invoke(this, new InvalidOperationException($"The mail view stopped ({args.Reason})."));
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private async void OpenLink(Uri uri, string? text)
    {
        NavigationRequested?.Invoke(this, new(uri, text));
        if (_linkLifetime.IsCancellationRequested) return;
        try
        {
            if (LinkOpener is { } opener) (await opener(uri, text)).ThrowIfNotSucceeded();
            else if (_launcher is not null) (await _launcher.LaunchUriAsync(uri, _linkLifetime.Token)).ThrowIfNotSucceeded();
        }
        catch (Exception error) { if (!_linkLifetime.IsCancellationRequested) OperationFailed?.Invoke(this, error); }
    }

    public ValueTask DisposeAsync(Action releaseVisual) => Queue.DisposeAsync(async () =>
    {
        await OnUIAsync(() =>
        {
            _disposed = true;
            _linkLifetime.Cancel();
            _launcher = null;
            LinkOpener = null;
            _sessionId = null;
            _ready.TrySetCanceled();
            Browser.StopLoading();
            _contents.UnregisterScriptMessageHandler(HandlerName, null);
            WebKit.UserContentManager.ScriptMessageReceivedSignal.Disconnect(_contents, ScriptMessageReceived);
            WebKit.WebView.DecidePolicySignal.Disconnect(Browser, DecidePolicy);
            WebKit.WebView.WebProcessTerminatedSignal.Disconnect(Browser, WebProcessTerminated);
            Message = null; NavigationRequested = null; OperationFailed = null; LinkHovered = null;
            releaseVisual();
            return Task.CompletedTask;
        });
    });
}
