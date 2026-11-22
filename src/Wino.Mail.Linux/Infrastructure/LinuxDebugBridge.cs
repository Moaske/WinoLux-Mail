#if DEBUG
using System.Collections.Concurrent;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// Debug-only bridge for development without screen access (the Mac MacDebugBridge's twin). It polls
/// <c>$XDG_CACHE_HOME/WinoLux/debug/cmd</c>; each line is one command, results are appended to <c>out.log</c> in the
/// same folder. Like the Mac bridge it only selects, navigates and focuses: no command ever fills a credential.
/// Built-in commands: <c>page [WinoPage]</c> (without a page: the current one), <c>focus NAME</c>, <c>commands</c>. Pages add their own with <see cref="Register"/>.
/// </summary>
public static class LinuxDebugBridge
{
    private static IDispatcher? _dispatcher;
    private static readonly ConcurrentDictionary<string, Func<string[], Task<string>>> Commands = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, WeakReference<Gtk.Widget>> FocusTargets = new(StringComparer.OrdinalIgnoreCase);
    private static Timer? _timer;
    private static int _polling;

    public static void Register(string name, Func<string[], Task<string>> command) => Commands[name] = command;

    /// <summary>A widget that <c>focus NAME</c> gives keyboard focus, so a test can type into it like a person.</summary>
    public static void RegisterFocus(string name, Gtk.Widget widget) => FocusTargets[name] = new WeakReference<Gtk.Widget>(widget);

    public static void Start(string cacheRoot, IDispatcher dispatcher, LinuxNavigationService navigation)
    {
        _dispatcher = dispatcher;
        var folder = Path.Combine(cacheRoot, "debug");
        Directory.CreateDirectory(folder);
        var commandFile = Path.Combine(folder, "cmd");
        var output = Path.Combine(folder, "out.log");
        Register("page", args => Task.FromResult(args.Length == 0 ? navigation.CurrentPageName ?? "none" : Enum.TryParse<WinoPage>(args[0], true, out var page)
            ? navigation.Navigate(page) ? "ok" : "not navigated" : "usage: page [WinoPage]"));
        Register("commands", _ => Task.FromResult(string.Join(' ', Commands.Keys.Order())));
        Register("focus", async args =>
        {
            if (args.Length == 0 || !FocusTargets.TryGetValue(args[0], out var reference) || !reference.TryGetTarget(out var widget))
                return "no focus target " + string.Join(' ', args) + "; known: " + string.Join(' ', FocusTargets.Keys.Order());
            var focused = false;
            await dispatcher.ExecuteOnUIThread(() => focused = widget.GrabFocus());
            return focused ? "focused" : "not focusable";
        });

        _timer = new Timer(_ => _ = PollAsync(commandFile, output), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300));
        Serilog.Log.Information("Debug bridge watching {File}", commandFile);
    }

    private static async Task PollAsync(string commandFile, string output)
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            if (!File.Exists(commandFile)) return;
            var lines = await File.ReadAllLinesAsync(commandFile);
            File.Delete(commandFile);
            foreach (var line in lines.Select(static line => line.Trim()).Where(static line => line.Length > 0))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string result;
                try
                {
                    if (Commands.TryGetValue(parts[0], out var command))
                    {
                        // Commands start on the UI thread, where GTK widgets and the ViewModels' UI state live.
                        Task<string> running = Task.FromResult(string.Empty);
                        var arguments = parts[1..];
                        await (_dispatcher?.ExecuteOnUIThread(() => running = command(arguments)) ?? Task.CompletedTask);
                        result = await running;
                    }
                    else result = "unknown command " + parts[0];
                }
                catch (Exception error) { result = "error: " + error.Message; }
                await File.AppendAllTextAsync(output, $"{line} => {result}{Environment.NewLine}");
            }
        }
        catch (Exception error) { Serilog.Log.Warning(error, "Debug bridge poll failed."); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }
}
#endif
